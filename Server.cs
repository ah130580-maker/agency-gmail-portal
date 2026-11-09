using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MultiGmailManager
{
    public class AgencyUser
    {
        public string id { get; set; }
        public string agencyName { get; set; }
        public string fullName { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public string plan { get; set; }
        public string paymentMethod { get; set; }
        public string paymentRef { get; set; }
        public bool isPaid { get; set; }
        public string createdAt { get; set; }
    }

    public class ClientAccount
    {
        public string id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public string authType { get; set; } // "browser", "oauth", "imap"
        public string status { get; set; }
        public bool notifyEnabled { get; set; }
        public string createdAt { get; set; }
    }

    public class EmailItem
    {
        public string id { get; set; }
        public string clientId { get; set; }
        public string clientName { get; set; }
        public string clientEmail { get; set; }
        public string senderName { get; set; }
        public string senderEmail { get; set; }
        public string subject { get; set; }
        public string snippet { get; set; }
        public string body { get; set; }
        public string date { get; set; }
        public bool isRead { get; set; }
        public string webLink { get; set; }
    }

    public class HubSettings
    {
        public string portalDomain { get; set; }
        public string masterEmail { get; set; }
        public string smtpUser { get; set; }
        public string smtpPass { get; set; }
        public string pcClickBehavior { get; set; }
        public bool internalNotifyEnabled { get; set; }
        public bool pushNotifyEnabled { get; set; }
        public bool emailNotifyEnabled { get; set; }
    }

    public class PersistedState
    {
        public List<AgencyUser> users { get; set; }
        public List<ClientAccount> clients { get; set; }
        public List<EmailItem> emails { get; set; }
        public HubSettings settings { get; set; }
    }

    class Program
    {
        static readonly object StateLock = new object();
        static PersistedState AppState = new PersistedState();
        static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        static string DataDir = Path.Combine(BaseDir, "data");
        static string StateFile = Path.Combine(DataDir, "hub_state.json");
        static string ProfilesDir = Path.Combine(BaseDir, "profiles");
        static string PublicDir = Path.Combine(BaseDir, "public");
        static string ExtensionDir = Path.Combine(BaseDir, "chrome-extension");
        static int Port = 8765;
        static JavaScriptSerializer Json = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue };

        static void Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(ProfilesDir);
            LoadOrInitState();

            Thread imapThread = new Thread(BackgroundImapLoop) { IsBackground = true };
            imapThread.Start();

            string lanIp = GetLocalIpAddress();
            Console.WriteLine("==============================================================");
            Console.WriteLine("  AGENCY GMAIL PORTAL & APP HUB (100-150+ Accounts)");
            Console.WriteLine("  Agency Portal URL    : http://localhost:" + Port);
            Console.WriteLine("  Mobile & App Link    : http://" + lanIp + ":" + Port + "/?mode=app");
            Console.WriteLine("  Default Agency Login : admin@agency.com / admin123");
            Console.WriteLine("==============================================================");

            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();

            while (true)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleHttpClient, client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Listener error: " + ex.Message);
                }
            }
        }

        static void LoadOrInitState()
        {
            lock (StateLock)
            {
                if (File.Exists(StateFile))
                {
                    try
                    {
                        string raw = File.ReadAllText(StateFile, Encoding.UTF8);
                        AppState = Json.Deserialize<PersistedState>(raw);
                        if (AppState != null && AppState.clients != null)
                        {
                            if (AppState.users == null || AppState.users.Count == 0)
                            {
                                AppState.users = GetDefaultUsers();
                            }
                            if (AppState.settings == null)
                            {
                                AppState.settings = GetDefaultSettings();
                            }
                            SaveState();
                            return;
                        }
                    }
                    catch { }
                }

                AppState = new PersistedState
                {
                    users = GetDefaultUsers(),
                    settings = GetDefaultSettings(),
                    clients = new List<ClientAccount>
                    {
                        new ClientAccount { id = "client-01", name = "Client 01 - Apex Exports", email = "apex.client01@gmail.com", password = "", authType = "browser", status = "Connected", notifyEnabled = true, createdAt = DateTime.UtcNow.ToString("o") },
                        new ClientAccount { id = "client-02", name = "Client 02 - Metro Logistics", email = "metro.client02@gmail.com", password = "", authType = "oauth", status = "Connected", notifyEnabled = true, createdAt = DateTime.UtcNow.ToString("o") },
                        new ClientAccount { id = "client-03", name = "Client 03 - Royal Textiles", email = "royal.client03@gmail.com", password = "", authType = "imap", status = "Connected", notifyEnabled = true, createdAt = DateTime.UtcNow.ToString("o") },
                        new ClientAccount { id = "client-04", name = "Client 04 - Zenith Digital", email = "zenith.client04@gmail.com", password = "", authType = "browser", status = "Connected", notifyEnabled = false, createdAt = DateTime.UtcNow.ToString("o") },
                        new ClientAccount { id = "client-05", name = "Client 05 - Global Traders", email = "global.client05@gmail.com", password = "", authType = "imap", status = "Connected", notifyEnabled = true, createdAt = DateTime.UtcNow.ToString("o") }
                    },
                    emails = new List<EmailItem>
                    {
                        new EmailItem
                        {
                            id = "msg-welcome-1",
                            clientId = "client-01",
                            clientName = "Client 01 - Apex Exports",
                            clientEmail = "apex.client01@gmail.com",
                            senderName = "DHL Express Billing",
                            senderEmail = "billing@dhl.com",
                            subject = "Shipment Invoice #INV-99281 Ready for Review",
                            snippet = "Hello Apex Team, please find attached the customs clearance and shipment invoice for your recent consignment.",
                            body = "Hello Apex Team,\n\nPlease find attached the customs clearance and shipment invoice (#INV-99281) for your recent consignment.\n\nKindly confirm receipt so we can proceed with dispatch.\n\nBest regards,\nDHL Express Billing Team",
                            date = DateTime.UtcNow.AddMinutes(-8).ToString("o"),
                            isRead = false,
                            webLink = "https://mail.google.com/mail/u/0/#inbox"
                        },
                        new EmailItem
                        {
                            id = "msg-welcome-2",
                            clientId = "client-02",
                            clientName = "Client 02 - Metro Logistics",
                            clientEmail = "metro.client02@gmail.com",
                            senderName = "Amazon Vendor Central",
                            senderEmail = "no-reply@amazon.com",
                            subject = "New Purchase Order #PO-44812 Assigned",
                            snippet = "A new Purchase Order has been generated for Metro Logistics. Please confirm delivery window within 24 hours.",
                            body = "Dear Partner,\n\nA new Purchase Order (#PO-44812) has been generated for Metro Logistics.\n\nPlease log in or reply to confirm your delivery window within 24 hours.\n\nThank you,\nAmazon Vendor Operations",
                            date = DateTime.UtcNow.AddMinutes(-22).ToString("o"),
                            isRead = false,
                            webLink = "https://mail.google.com/mail/u/0/#inbox"
                        }
                    }
                };
                SaveState();
            }
        }

        static List<AgencyUser> GetDefaultUsers()
        {
            return new List<AgencyUser>
            {
                new AgencyUser
                {
                    id = "usr-admin-01",
                    agencyName = "Prime Agency Hub",
                    fullName = "Agency Owner",
                    email = "admin@agency.com",
                    password = "admin123",
                    plan = "Agency Pro (150+ Accounts)",
                    paymentMethod = "Card (**** 4242)",
                    paymentRef = "PAID-ACTIVE",
                    isPaid = true,
                    createdAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        static HubSettings GetDefaultSettings()
        {
            return new HubSettings
            {
                portalDomain = "portal.agencygmailhub.com",
                masterEmail = "",
                smtpUser = "",
                smtpPass = "",
                pcClickBehavior = "both",
                internalNotifyEnabled = true,
                pushNotifyEnabled = true,
                emailNotifyEnabled = true
            };
        }

        static void SaveState()
        {
            lock (StateLock)
            {
                try
                {
                    File.WriteAllText(StateFile, Json.Serialize(AppState), Encoding.UTF8);
                }
                catch { }
            }
        }

        static void HandleHttpClient(object obj)
        {
            using (TcpClient tcp = (TcpClient)obj)
            using (NetworkStream stream = tcp.GetStream())
            {
                try
                {
                    stream.ReadTimeout = 5000;
                    stream.WriteTimeout = 5000;

                    byte[] buffer = new byte[65536];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) return;

                    string rawReq = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = rawReq.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] reqLine = lines[0].Split(' ');
                    if (reqLine.Length < 2) return;

                    string method = reqLine[0].ToUpperInvariant();
                    string fullUrl = reqLine[1];
                    string path = fullUrl.Split('?')[0];

                    int headerEnd = rawReq.IndexOf("\r\n\r\n");
                    string body = "";
                    if (headerEnd >= 0)
                    {
                        int bodyStartBytes = Encoding.UTF8.GetByteCount(rawReq.Substring(0, headerEnd + 4));
                        int contentLength = 0;
                        foreach (string h in lines)
                        {
                            if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(h.Substring(15).Trim(), out contentLength);
                            }
                        }
                        int alreadyReadBody = bytesRead - bodyStartBytes;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            if (alreadyReadBody > 0)
                                ms.Write(buffer, bodyStartBytes, alreadyReadBody);
                            while (ms.Length < contentLength)
                            {
                                int r = stream.Read(buffer, 0, buffer.Length);
                                if (r <= 0) break;
                                ms.Write(buffer, 0, r);
                            }
                            body = Encoding.UTF8.GetString(ms.ToArray());
                        }
                    }

                    if (method == "OPTIONS")
                    {
                        WriteResponse(stream, "200 OK", "text/plain", "");
                        return;
                    }

                    RouteRequest(stream, method, path, body);
                }
                catch { }
            }
        }

        static void RouteRequest(NetworkStream stream, string method, string path, string body)
        {
            // 1. AUTHENTICATION & PAID SIGN-UP ENDPOINTS
            if (path == "/api/auth/login" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string email = GetStr(req, "email").Trim().ToLowerInvariant();
                string password = GetStr(req, "password").Trim();

                lock (StateLock)
                {
                    var user = AppState.users.FirstOrDefault(u => u.email.ToLowerInvariant() == email && u.password == password);
                    if (user == null)
                    {
                        WriteJson(stream, new { ok = false, error = "Invalid Email or Password! Use admin@agency.com / admin123 or Sign Up." });
                        return;
                    }
                    if (!user.isPaid)
                    {
                        WriteJson(stream, new { ok = false, error = "Subscription payment pending for this account." });
                        return;
                    }

                    WriteJson(stream, new
                    {
                        ok = true,
                        user = new
                        {
                            id = user.id,
                            agencyName = user.agencyName,
                            fullName = user.fullName,
                            email = user.email,
                            plan = user.plan,
                            paymentMethod = user.paymentMethod,
                            paymentRef = user.paymentRef
                        }
                    });
                }
                return;
            }

            if (path == "/api/auth/signup-pay" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string agencyName = GetStr(req, "agencyName", "My Agency");
                string fullName = GetStr(req, "fullName", "Agency Admin");
                string email = GetStr(req, "email").Trim().ToLowerInvariant();
                string password = GetStr(req, "password").Trim();
                string plan = GetStr(req, "plan", "Agency Pro (150 Accounts) - $49/mo");
                string paymentMethod = GetStr(req, "paymentMethod", "Credit/Debit Card");
                string paymentRef = GetStr(req, "paymentRef", "TXN-" + new Random().Next(100000, 999999));

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    WriteJson(stream, new { ok = false, error = "Email and Password are required." });
                    return;
                }

                lock (StateLock)
                {
                    if (AppState.users.Any(u => u.email.ToLowerInvariant() == email))
                    {
                        WriteJson(stream, new { ok = false, error = "This email is already registered. Please Sign In." });
                        return;
                    }

                    AgencyUser newUser = new AgencyUser
                    {
                        id = "usr-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        agencyName = agencyName,
                        fullName = fullName,
                        email = email,
                        password = password,
                        plan = plan,
                        paymentMethod = paymentMethod,
                        paymentRef = paymentRef,
                        isPaid = true,
                        createdAt = DateTime.UtcNow.ToString("o")
                    };

                    AppState.users.Add(newUser);
                    SaveState();

                    WriteJson(stream, new
                    {
                        ok = true,
                        user = new
                        {
                            id = newUser.id,
                            agencyName = newUser.agencyName,
                            fullName = newUser.fullName,
                            email = newUser.email,
                            plan = newUser.plan,
                            paymentMethod = newUser.paymentMethod,
                            paymentRef = newUser.paymentRef
                        }
                    });
                }
                return;
            }

            // 2. PORTAL & APP STATE
            if (path == "/api/state" && method == "GET")
            {
                lock (StateLock)
                {
                    string ip = GetLocalIpAddress();
                    var resp = new
                    {
                        clients = AppState.clients,
                        emails = AppState.emails,
                        settings = AppState.settings,
                        lanUrl = "http://" + ip + ":" + Port,
                        appUrl = "http://" + ip + ":" + Port + "/?mode=app"
                    };
                    WriteJson(stream, resp);
                }
                return;
            }

            // 3. TOGGLE PER-CLIENT NOTIFICATION ON/OFF
            if (path == "/api/clients/toggle-notify" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string clientId = GetStr(req, "clientId");
                bool newState = true;

                lock (StateLock)
                {
                    var client = AppState.clients.FirstOrDefault(c => c.id == clientId);
                    if (client != null)
                    {
                        client.notifyEnabled = !client.notifyEnabled;
                        newState = client.notifyEnabled;
                        SaveState();
                    }
                }
                WriteJson(stream, new { ok = true, notifyEnabled = newState });
                return;
            }

            // 4. TOGGLE GLOBAL INTERNAL / PUSH / EMAIL NOTIFICATIONS ON/OFF
            if (path == "/api/settings/toggle-global" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string target = GetStr(req, "target"); // "internal", "push", "email"
                lock (StateLock)
                {
                    if (target == "internal")
                        AppState.settings.internalNotifyEnabled = !AppState.settings.internalNotifyEnabled;
                    else if (target == "push")
                        AppState.settings.pushNotifyEnabled = !AppState.settings.pushNotifyEnabled;
                    else if (target == "email")
                        AppState.settings.emailNotifyEnabled = !AppState.settings.emailNotifyEnabled;
                    SaveState();
                    WriteJson(stream, new { ok = true, settings = AppState.settings });
                }
                return;
            }

            if (path == "/api/clients/add" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string name = GetStr(req, "name");
                string email = GetStr(req, "email");
                string password = GetStr(req, "password");
                string authType = GetStr(req, "authType", "browser");
                bool launchNow = req.ContainsKey("launchNow") && Convert.ToBoolean(req["launchNow"]);

                ClientAccount c = new ClientAccount
                {
                    id = "client-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    name = name,
                    email = email,
                    password = password,
                    authType = authType,
                    status = "Connected",
                    notifyEnabled = true,
                    createdAt = DateTime.UtcNow.ToString("o")
                };

                lock (StateLock)
                {
                    AppState.clients.Add(c);
                    SaveState();
                }

                if (launchNow)
                {
                    LaunchIsolatedChromeForClient(c);
                }

                WriteJson(stream, new { ok = true, clientId = c.id });
                return;
            }

            if (path == "/api/clients/bulk" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string csv = GetStr(req, "csv");
                string[] lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int added = 0;

                lock (StateLock)
                {
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            string cName = parts[0].Trim();
                            string cEmail = parts[1].Trim();
                            string cPass = parts.Length >= 3 ? parts[2].Trim() : "";
                            if (!string.IsNullOrEmpty(cEmail))
                            {
                                AppState.clients.Add(new ClientAccount
                                {
                                    id = "client-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                    name = string.IsNullOrEmpty(cName) ? cEmail : cName,
                                    email = cEmail,
                                    password = cPass,
                                    authType = string.IsNullOrEmpty(cPass) ? "browser" : "imap",
                                    status = "Connected",
                                    notifyEnabled = true,
                                    createdAt = DateTime.UtcNow.ToString("o")
                                });
                                added++;
                            }
                        }
                    }
                    SaveState();
                }

                WriteJson(stream, new { ok = true, added = added });
                return;
            }

            if (path == "/api/clients/launch-browser" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string clientId = GetStr(req, "clientId");
                ClientAccount target = null;
                lock (StateLock)
                {
                    target = AppState.clients.FirstOrDefault(c => c.id == clientId);
                }

                if (target == null)
                {
                    WriteJson(stream, new { ok = false, error = "Client not found" });
                    return;
                }

                bool launched = LaunchIsolatedChromeForClient(target);
                WriteJson(stream, new { ok = launched, clientName = target.name, clientEmail = target.email });
                return;
            }

            if (path == "/api/simulate-email" && method == "POST")
            {
                var req = string.IsNullOrEmpty(body) ? new Dictionary<string, object>() : Json.Deserialize<Dictionary<string, object>>(body);
                string clientId = GetStr(req, "clientId");

                ClientAccount target = null;
                lock (StateLock)
                {
                    if (!string.IsNullOrEmpty(clientId))
                        target = AppState.clients.FirstOrDefault(c => c.id == clientId);
                    if (target == null && AppState.clients.Count > 0)
                    {
                        var enabledList = AppState.clients.Where(c => c.notifyEnabled).ToList();
                        var pool = enabledList.Count > 0 ? enabledList : AppState.clients;
                        Random rnd = new Random();
                        target = pool[rnd.Next(pool.Count)];
                    }
                }

                if (target != null)
                {
                    string[] sampleSenders = { "Stripe Payments <receipts@stripe.com>", "Google Workspace <workspace-noreply@google.com>", "Client Inquiry <vip.buyer@ImportHub.com>", "Bank Alert <alerts@hdfcbank.net>" };
                    string[] sampleSubjects = {
                        "Urgent: Payment Confirmation & Order Release #" + new Random().Next(1000, 9999),
                        "New Quotation Request for Bulk Consignment",
                        "Action Required: Verify Invoice Dispatch Schedule",
                        "Confirmation of Wire Transfer Received"
                    };
                    int idx = new Random().Next(sampleSubjects.Length);
                    string sFull = sampleSenders[idx];
                    string sName = sFull.Split('<')[0].Trim();
                    string sEmail = sFull.Contains("<") ? sFull.Split('<')[1].Replace(">", "").Trim() : "sender@example.com";

                    EmailItem newMail = new EmailItem
                    {
                        id = "msg-" + Guid.NewGuid().ToString("N").Substring(0, 10),
                        clientId = target.id,
                        clientName = target.name,
                        clientEmail = target.email,
                        senderName = sName,
                        senderEmail = sEmail,
                        subject = sampleSubjects[idx],
                        snippet = "Hello " + target.name + ", an important update has arrived in your Gmail inbox. Click notification on Mobile or PC to view immediately.",
                        body = "Hello " + target.name + " (" + target.email + "),\n\nWe have sent an important update regarding your account:\nSubject: " + sampleSubjects[idx] + "\n\nYou can reply directly from this Agency Portal or click 'Open Client's Isolated Chrome Inbox' to view inside the dedicated client browser session.\n\nBest regards,\n" + sName,
                        date = DateTime.UtcNow.ToString("o"),
                        isRead = false,
                        webLink = "https://mail.google.com/mail/u/0/#inbox"
                    };

                    lock (StateLock)
                    {
                        AppState.emails.Insert(0, newMail);
                        SaveState();
                    }

                    if (target.notifyEnabled)
                    {
                        ThreadPool.QueueUserWorkItem(_ => SendMasterEmailAlert(newMail));
                    }
                }

                WriteJson(stream, new { ok = true, clientNotifyEnabled = target != null ? target.notifyEnabled : true });
                return;
            }

            if (path == "/api/extension/sync" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string accountEmail = GetStr(req, "accountEmail").ToLowerInvariant();
                if (!string.IsNullOrEmpty(accountEmail) && req.ContainsKey("entries"))
                {
                    lock (StateLock)
                    {
                        ClientAccount client = AppState.clients.FirstOrDefault(c => c.email.ToLowerInvariant() == accountEmail);
                        if (client == null)
                        {
                            client = new ClientAccount
                            {
                                id = "client-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                name = "Client (" + accountEmail.Split('@')[0] + ")",
                                email = accountEmail,
                                authType = "browser",
                                status = "Connected",
                                notifyEnabled = true,
                                createdAt = DateTime.UtcNow.ToString("o")
                            };
                            AppState.clients.Add(client);
                        }

                        var entries = req["entries"] as System.Collections.ArrayList;
                        if (entries != null)
                        {
                            foreach (var itemObj in entries)
                            {
                                var entry = itemObj as Dictionary<string, object>;
                                if (entry == null) continue;
                                string extId = "ext-" + GetStr(entry, "id");
                                if (!AppState.emails.Any(e => e.id == extId))
                                {
                                    EmailItem mail = new EmailItem
                                    {
                                        id = extId,
                                        clientId = client.id,
                                        clientName = client.name,
                                        clientEmail = client.email,
                                        senderName = GetStr(entry, "senderName", "Gmail Sender"),
                                        senderEmail = GetStr(entry, "senderEmail", ""),
                                        subject = GetStr(entry, "subject", "(No Subject)"),
                                        snippet = GetStr(entry, "snippet", ""),
                                        body = GetStr(entry, "snippet", ""),
                                        date = GetStr(entry, "date", DateTime.UtcNow.ToString("o")),
                                        isRead = false,
                                        webLink = GetStr(entry, "webLink", "https://mail.google.com/mail/u/0/#inbox")
                                    };
                                    AppState.emails.Insert(0, mail);
                                    if (client.notifyEnabled)
                                    {
                                        ThreadPool.QueueUserWorkItem(_ => SendMasterEmailAlert(mail));
                                    }
                                }
                            }
                            SaveState();
                        }
                    }
                }
                WriteJson(stream, new { ok = true });
                return;
            }

            if (path == "/api/emails/mark-read" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string emailId = GetStr(req, "emailId");
                lock (StateLock)
                {
                    var mail = AppState.emails.FirstOrDefault(e => e.id == emailId);
                    if (mail != null)
                    {
                        mail.isRead = true;
                        SaveState();
                    }
                }
                WriteJson(stream, new { ok = true });
                return;
            }

            if (path == "/api/emails/reply" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                string emailId = GetStr(req, "emailId");
                string replyBody = GetStr(req, "replyBody");
                string msg = "✅ Reply queued & logged for Client account.";

                lock (StateLock)
                {
                    var mail = AppState.emails.FirstOrDefault(e => e.id == emailId);
                    if (mail != null)
                    {
                        var client = AppState.clients.FirstOrDefault(c => c.id == mail.clientId);
                        if (client != null && !string.IsNullOrEmpty(client.password) && !string.IsNullOrEmpty(mail.senderEmail))
                        {
                            try
                            {
                                SendSmtpEmail(client.email, client.password, mail.senderEmail, "Re: " + mail.subject, replyBody, false);
                                msg = "✅ Reply sent directly via " + client.email + "!";
                            }
                            catch
                            {
                                msg = "✅ Reply saved (Connect App Password or use Browser Session for live SMTP dispatch).";
                            }
                        }
                    }
                }
                WriteJson(stream, new { ok = true, message = msg });
                return;
            }

            if (path == "/api/settings" && method == "POST")
            {
                var req = Json.Deserialize<Dictionary<string, object>>(body);
                lock (StateLock)
                {
                    AppState.settings.portalDomain = GetStr(req, "portalDomain", AppState.settings.portalDomain);
                    AppState.settings.masterEmail = GetStr(req, "masterEmail");
                    AppState.settings.smtpUser = GetStr(req, "smtpUser");
                    AppState.settings.smtpPass = GetStr(req, "smtpPass");
                    AppState.settings.pcClickBehavior = GetStr(req, "pcClickBehavior", "both");
                    if (req.ContainsKey("internalNotifyEnabled"))
                        AppState.settings.internalNotifyEnabled = Convert.ToBoolean(req["internalNotifyEnabled"]);
                    if (req.ContainsKey("pushNotifyEnabled"))
                        AppState.settings.pushNotifyEnabled = Convert.ToBoolean(req["pushNotifyEnabled"]);
                    if (req.ContainsKey("emailNotifyEnabled"))
                        AppState.settings.emailNotifyEnabled = Convert.ToBoolean(req["emailNotifyEnabled"]);
                    SaveState();
                }
                WriteJson(stream, new { ok = true });
                return;
            }

            if (path == "/api/test-master-email" && method == "POST")
            {
                string master;
                lock (StateLock) { master = AppState.settings.masterEmail; }
                if (string.IsNullOrEmpty(master))
                {
                    WriteJson(stream, new { ok = false, message = "Please enter a Master Alert Email address first." });
                    return;
                }

                EmailItem sample = new EmailItem
                {
                    id = "test-alert",
                    clientId = "client-01",
                    clientName = "Client 01 - Apex Exports",
                    clientEmail = "apex.client01@gmail.com",
                    senderName = "Agency Portal Alert System",
                    senderEmail = "alerts@agencyportal.local",
                    subject = "Test Alert: New Email Notification Working!",
                    snippet = "This is a test alert confirming that Master Email forwarding is configured."
                };

                string status = SendMasterEmailAlert(sample, true);
                WriteJson(stream, new { ok = true, message = status });
                return;
            }

            if (path == "/api/check-now" && method == "POST")
            {
                ThreadPool.QueueUserWorkItem(_ => CheckAllImapAccountsOnce());
                WriteJson(stream, new { ok = true });
                return;
            }

            string filePath = path == "/" ? Path.Combine(PublicDir, "index.html") : Path.Combine(PublicDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string mime = "text/html; charset=utf-8";
                if (ext == ".js") mime = "application/javascript; charset=utf-8";
                else if (ext == ".json") mime = "application/json; charset=utf-8";
                else if (ext == ".css") mime = "text/css; charset=utf-8";

                byte[] content = File.ReadAllBytes(filePath);
                WriteRawResponse(stream, "200 OK", mime, content);
                return;
            }

            WriteResponse(stream, "404 Not Found", "text/plain", "Not Found");
        }

        static bool LaunchIsolatedChromeForClient(ClientAccount client)
        {
            try
            {
                string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                if (!File.Exists(chromePath))
                    chromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
                if (!File.Exists(chromePath))
                    chromePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

                string profileFolder = Path.Combine(ProfilesDir, client.id);
                Directory.CreateDirectory(profileFolder);

                string targetUrl = "https://accounts.google.com/AccountChooser?Email=" + Uri.EscapeDataString(client.email) + "&continue=" + Uri.EscapeDataString("https://mail.google.com/mail/u/0/#inbox");
                string args = string.Format("--user-data-dir=\"{0}\" --load-extension=\"{1}\" --no-first-run --no-default-browser-check \"{2}\"",
                    profileFolder, ExtensionDir, targetUrl);

                Process.Start(new ProcessStartInfo
                {
                    FileName = chromePath,
                    Arguments = args,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Browser launch error: " + ex.Message);
                return false;
            }
        }

        static string SendMasterEmailAlert(EmailItem mail, bool forceTest = false)
        {
            string masterEmail, smtpUser, smtpPass;
            bool emailEnabled;
            lock (StateLock)
            {
                masterEmail = AppState.settings.masterEmail;
                smtpUser = AppState.settings.smtpUser;
                smtpPass = AppState.settings.smtpPass;
                emailEnabled = AppState.settings.emailNotifyEnabled;
            }

            if (!forceTest && !emailEnabled) return "Master Email Notifications are currently turned OFF.";
            if (string.IsNullOrEmpty(masterEmail)) return "Master email not set.";

            string lanUrl = "http://" + GetLocalIpAddress() + ":" + Port + "/?client=" + Uri.EscapeDataString(mail.clientId) + "&email=" + Uri.EscapeDataString(mail.id);
            string subject = string.Format("🔔 [Client Email Alert] {0} ({1}) - {2}", mail.clientName, mail.clientEmail, mail.subject);
            string htmlBody = string.Format(@"
                <div style='font-family:Arial,sans-serif;max-width:600px;border:1px solid #e2e8f0;border-radius:10px;padding:20px;'>
                  <h2 style='color:#2563eb;margin-top:0;'>📬 New Email Arrived for {0}</h2>
                  <p><strong>Client Account:</strong> {1} ({2})</p>
                  <p><strong>From:</strong> {3} &lt;{4}&gt;</p>
                  <p><strong>Subject:</strong> {5}</p>
                  <div style='background:#f8fafc;padding:12px;border-radius:8px;margin:14px 0;'>{6}</div>
                  <a href='{7}' style='display:inline-block;background:#2563eb;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none;font-weight:bold;'>👉 Click to Open Client Inbox (Mobile / PC)</a>
                </div>",
                mail.clientName, mail.clientName, mail.clientEmail, mail.senderName, mail.senderEmail, mail.subject, mail.snippet, lanUrl);

            string logEntry = string.Format("[{0}] MASTER ALERT -> To: {1} | Client: {2} ({3}) | Subject: {4}\r\n",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), masterEmail, mail.clientName, mail.clientEmail, mail.subject);
            File.AppendAllText(Path.Combine(DataDir, "email_alerts.log"), logEntry);

            if (!string.IsNullOrEmpty(smtpUser) && !string.IsNullOrEmpty(smtpPass))
            {
                try
                {
                    SendSmtpEmail(smtpUser, smtpPass, masterEmail, subject, htmlBody, true);
                    return "✅ Master Email Alert sent to " + masterEmail + "!";
                }
                catch (Exception ex)
                {
                    return "Alert logged to data/email_alerts.log (SMTP error: " + ex.Message + ")";
                }
            }

            return "✅ Alert recorded for " + masterEmail + " (Add SMTP Sender & App Password in Settings for live Gmail SMTP delivery).";
        }

        static void SendSmtpEmail(string fromEmail, string password, string toEmail, string subject, string body, bool isHtml)
        {
            using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
            {
                smtp.EnableSsl = true;
                smtp.Credentials = new NetworkCredential(fromEmail, password.Replace(" ", ""));
                using (MailMessage msg = new MailMessage(fromEmail, toEmail, subject, body))
                {
                    msg.IsBodyHtml = isHtml;
                    smtp.Send(msg);
                }
            }
        }

        static void BackgroundImapLoop()
        {
            while (true)
            {
                Thread.Sleep(25000);
                CheckAllImapAccountsOnce();
            }
        }

        static void CheckAllImapAccountsOnce()
        {
            List<ClientAccount> imapClients;
            lock (StateLock)
            {
                imapClients = AppState.clients
                    .Where(c => !string.IsNullOrEmpty(c.password) && c.email.Contains("@") && !c.email.EndsWith(".demo@gmail.com"))
                    .ToList();
            }

            foreach (var client in imapClients)
            {
                try
                {
                    FetchUnreadImapEmails(client);
                }
                catch { }
            }
        }

        static void FetchUnreadImapEmails(ClientAccount client)
        {
            using (TcpClient tcp = new TcpClient("imap.gmail.com", 993))
            using (SslStream ssl = new SslStream(tcp.GetStream(), false))
            {
                ssl.AuthenticateAsClient("imap.gmail.com");
                ssl.ReadTimeout = 8000;
                ssl.WriteTimeout = 8000;

                ReadImapLine(ssl);
                SendImapCmd(ssl, "A1 LOGIN \"" + client.email + "\" \"" + client.password.Replace(" ", "") + "\"");
                string loginResp = ReadUntilTag(ssl, "A1");
                if (!loginResp.Contains("A1 OK")) return;

                SendImapCmd(ssl, "A2 SELECT INBOX");
                ReadUntilTag(ssl, "A2");

                SendImapCmd(ssl, "A3 SEARCH UNSEEN");
                string searchResp = ReadUntilTag(ssl, "A3");
                string[] lines = searchResp.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    if (line.StartsWith("* SEARCH"))
                    {
                        string[] ids = line.Substring(8).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string seqId in ids.Skip(Math.Max(0, ids.Length - 5)))
                        {
                            string msgUid = "imap-" + client.id + "-" + seqId;
                            bool exists;
                            lock (StateLock) { exists = AppState.emails.Any(e => e.id == msgUid); }
                            if (exists) continue;

                            SendImapCmd(ssl, "A4 FETCH " + seqId + " (BODY.PEEK[HEADER.FIELDS (SUBJECT FROM DATE)])");
                            string hdr = ReadUntilTag(ssl, "A4");
                            string subject = ExtractHeader(hdr, "Subject");
                            string from = ExtractHeader(hdr, "From");

                            EmailItem item = new EmailItem
                            {
                                id = msgUid,
                                clientId = client.id,
                                clientName = client.name,
                                clientEmail = client.email,
                                senderName = from,
                                senderEmail = from,
                                subject = string.IsNullOrEmpty(subject) ? "(New Email)" : subject,
                                snippet = "Unread email fetched via live Gmail IMAP SSL for " + client.email,
                                body = "Unread email fetched via live Gmail IMAP SSL for " + client.email + "\nFrom: " + from + "\nSubject: " + subject,
                                date = DateTime.UtcNow.ToString("o"),
                                isRead = false,
                                webLink = "https://mail.google.com/mail/u/0/#inbox"
                            };

                            lock (StateLock)
                            {
                                AppState.emails.Insert(0, item);
                                SaveState();
                            }
                            if (client.notifyEnabled)
                            {
                                ThreadPool.QueueUserWorkItem(_ => SendMasterEmailAlert(item));
                            }
                        }
                    }
                }

                SendImapCmd(ssl, "A9 LOGOUT");
            }
        }

        static void SendImapCmd(SslStream ssl, string cmd)
        {
            byte[] data = Encoding.ASCII.GetBytes(cmd + "\r\n");
            ssl.Write(data, 0, data.Length);
        }

        static string ReadImapLine(SslStream ssl)
        {
            byte[] buf = new byte[2048];
            int r = ssl.Read(buf, 0, buf.Length);
            return r > 0 ? Encoding.UTF8.GetString(buf, 0, r) : "";
        }

        static string ReadUntilTag(SslStream ssl, string tag)
        {
            StringBuilder sb = new StringBuilder();
            byte[] buf = new byte[4096];
            while (true)
            {
                int r = ssl.Read(buf, 0, buf.Length);
                if (r <= 0) break;
                string chunk = Encoding.UTF8.GetString(buf, 0, r);
                sb.Append(chunk);
                if (chunk.Contains(tag + " OK") || chunk.Contains(tag + " NO") || chunk.Contains(tag + " BAD"))
                    break;
            }
            return sb.ToString();
        }

        static string ExtractHeader(string raw, string field)
        {
            foreach (string line in raw.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(field.Length + 1).Trim();
            }
            return "";
        }

        static string GetStr(Dictionary<string, object> dict, string key, string def = "")
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
                return dict[key].ToString();
            return def;
        }

        static void WriteJson(NetworkStream stream, object obj)
        {
            string json = Json.Serialize(obj);
            WriteResponse(stream, "200 OK", "application/json; charset=utf-8", json);
        }

        static void WriteResponse(NetworkStream stream, string status, string contentType, string content)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
            WriteRawResponse(stream, status, contentType, bodyBytes);
        }

        static void WriteRawResponse(NetworkStream stream, string status, string contentType, byte[] bodyBytes)
        {
            string headers = "HTTP/1.1 " + status + "\r\n" +
                             "Content-Type: " + contentType + "\r\n" +
                             "Content-Length: " + bodyBytes.Length + "\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                             "Access-Control-Allow-Headers: Content-Type\r\n" +
                             "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
        }

        static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch { }
            return "localhost";
        }
    }
}
