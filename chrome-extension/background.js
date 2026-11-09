const HUB_URL = "http://localhost:8765/api/extension/sync";

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create("checkGmailAtom", { periodInMinutes: 0.4 });
  checkGmailFeed();
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "checkGmailAtom") {
    checkGmailFeed();
  }
});

async function checkGmailFeed() {
  try {
    const response = await fetch("https://mail.google.com/mail/feed/atom?_=" + Date.now(), {
      credentials: "include",
      cache: "no-store"
    });

    if (!response.ok) return;
    const xmlText = await response.text();

    // Extract account email from feed title: "Gmail - Inbox for example@gmail.com"
    const titleMatch = xmlText.match(/<title>Gmail - Inbox for ([^<]+)<\/title>/i);
    const accountEmail = titleMatch ? titleMatch[1].trim() : "";
    if (!accountEmail) return;

    const fullCountMatch = xmlText.match(/<fullcount>(\d+)<\/fullcount>/i);
    const unreadCount = fullCountMatch ? parseInt(fullCountMatch[1], 10) : 0;

    // Extract entries
    const entries = [];
    const entryRegex = /<entry>([\s\S]*?)<\/entry>/gi;
    let match;
    while ((match = entryRegex.exec(xmlText)) !== null) {
      const block = match[1];
      const getTag = (tag) => {
        const m = block.match(new RegExp(`<${tag}[^>]*>([\\s\\S]*?)<\\/${tag}>`, "i"));
        return m ? decodeXml(m[1].trim()) : "";
      };
      const linkMatch = block.match(/<link[^>]*href="([^"]+)"/i);

      entries.push({
        id: getTag("id") || ("ext-" + Date.now() + "-" + Math.random()),
        subject: getTag("title") || "(No Subject)",
        snippet: getTag("summary") || "",
        senderName: getTag("name") || "Unknown",
        senderEmail: getTag("email") || "",
        date: getTag("issued") || new Date().toISOString(),
        webLink: linkMatch ? decodeXml(linkMatch[1]) : "https://mail.google.com/mail/u/0/#inbox"
      });
    }

    await fetch(HUB_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        accountEmail,
        unreadCount,
        entries
      })
    });
  } catch (err) {
    // Hub or network temporarily unreachable
  }
}

function decodeXml(str) {
  return str
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'");
}
