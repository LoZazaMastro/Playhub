"""Bounded RSS news for the Home. No article bodies, browser cookies or secrets."""
import datetime
import concurrent.futures
import base64
import email.utils
import gzip
import html
import http.cookiejar
import json
import os
import re
import threading
import time
import urllib.parse
import urllib.request

TTL = 1800
# Gamebase currently publishes a valid feed a little above 2 MiB. Four MiB
# remains a small, bounded document while avoiding an unnecessary aggregator
# fallback for a feed that already contains its lead images.
LIMIT = 4 * 1024 * 1024
REGIONS = {"en": "US", "it": "IT", "de": "DE", "es": "ES", "fr": "FR", "pt": "BR", "ru": "RU", "uk": "UA", "ja": "JP", "ko": "KR", "zh": "TW", "hi": "IN"}
# Editorial allowlist: country -> (publisher, article host, optional publisher RSS).
# Search RSS is used only as a transport for one listed publisher, never a topic search.
SOURCES = {
 "US": [("IGN", "ign.com", "https://feeds.feedburner.com/ign/all"), ("GameSpot", "gamespot.com", "https://www.gamespot.com/feeds/mashup/")],
 "IT": [("Multiplayer.it", "multiplayer.it", "https://multiplayer.it/feed/rss/homepage/"), ("IGN Italia", "it.ign.com", "https://it.ign.com/feed.xml")],
 "DE": [("GameStar", "gamestar.de", "https://www.gamestar.de/news/rss/news.rss"), ("GamePro", "gamepro.de", "https://www.gamepro.de/news/rss/news.rss")],
 "ES": [("3DJuegos", "3djuegos.com", "https://www.3djuegos.com/feedburner.xml"), ("Vandal", "vandal.elespanol.com", "https://vandal.elespanol.com/xml.cgi?rss=2")],
 "FR": [("Gamekult", "gamekult.com", "https://www.gamekult.com/feed.xml"), ("Jeuxvideo.com", "jeuxvideo.com", "https://www.jeuxvideo.com/rss/rss.xml")],
 # GameVicio's own feed labels YouTube embeds as media and its CDN intermittently
 # rejects image requests without a site Referer. The search RSS thumbnail is stable.
 "BR": [("GameVicio", "gamevicio.com", ""), ("The Enemy", "theenemy.com.br", "")],
 "RU": [("Игромания", "igromania.ru", "https://www.igromania.ru/rss/news.xml"), ("GameMAG", "gamemag.ru", "https://gamemag.ru/rss/feed")],
 "UA": [("PlayUA", "playua.net", "https://playua.net/feed/")],
 "JP": [("4Gamer", "4gamer.net", "https://www.4gamer.net/rss/news_topics.xml"), ("Famitsu", "famitsu.com", "")],
 "KR": [("GameMeca", "gamemeca.com", "https://www.gamemeca.com/rss.php"), ("Inven", "inven.co.kr", "https://feeds.feedburner.com/inven")],
 "TW": [("巴哈姆特 GNN", "gnn.gamer.com.tw", "https://gnn.gamer.com.tw/rss.xml"), ("遊戲基地", "gamebase.com.tw", "https://news.gamebase.com.tw/rss.xml")],
 "IN": [("IGN India", "in.ign.com", "https://in.ign.com/feed.xml"), ("AFK Gaming", "afkgaming.com", "")],
}
SOURCE_LANGUAGES = {country: language for language, country in REGIONS.items()}
SOURCE_LANGUAGES["IN"] = "en"  # These Indian specialist editions publish in English.

def _is_google_news_url(value):
    try:
        host = (urllib.parse.urlsplit(value).hostname or "").lower()
        return host == "news.google.com" or host.endswith(".news.google.com")
    except ValueError:
        return False

def _google_legacy_url(value):
    """Decode the old self-contained Google News token, when it is present."""
    try:
        token = urllib.parse.urlsplit(value).path.rstrip("/").split("/")[-1]
        raw = base64.urlsafe_b64decode(token + "=" * ((4 - len(token) % 4) % 4))
        if raw.startswith(b"\x08\x13\x22"): raw = raw[3:]
        if raw.endswith(b"\xd2\x01\x00"): raw = raw[:-3]
        if not raw: return ""
        length, offset = raw[0], 1
        if length >= 0x80 and len(raw) > 1:
            length, offset = (length & 0x7f) | (raw[1] << 7), 2
        result = raw[offset:offset + length].decode("utf-8", "ignore").strip()
        return safe_url(result)
    except Exception:
        return ""

def _google_decode_params(document):
    def first(patterns):
        for pattern in patterns:
            match = re.search(pattern, document, re.I)
            if match: return html.unescape(match.group(1))
        return ""
    return (
        first((r'data-n-a-id=["\']([^"\']+)', r'"source"\s*:\s*"([^"]+)"')),
        first((r'data-n-a-sg=["\']([^"\']+)', r'"signature"\s*:\s*"([^"]+)"')),
        first((r'data-n-a-ts=["\']([^"\']+)', r'"timestamp"\s*:\s*"?([0-9]+)')),
    )

def _google_decoded_url(payload):
    def visit(value):
        if isinstance(value, str):
            candidate = html.unescape(value).replace("\\/", "/").strip()
            if safe_url(candidate) and not _is_google_news_url(candidate):
                host = (urllib.parse.urlsplit(candidate).hostname or "").lower()
                if host and "google." not in host and not host.endswith(("gstatic.com", "googleusercontent.com")):
                    return candidate
            try: return visit(json.loads(value))
            except (ValueError, TypeError): return ""
        if isinstance(value, list):
            for child in value:
                result = visit(child)
                if result: return result
        if isinstance(value, dict):
            return visit(list(value.values()))
        return ""
    for segment in str(payload or "").split("\n\n"):
        chunk = segment.strip()
        if chunk.startswith(")]}'"): chunk = chunk.split("\n", 1)[-1]
        try: result = visit(json.loads(chunk))
        except (ValueError, TypeError): continue
        if result: return result
    return ""

def publisher_url(value, domain):
    try:
        host = urllib.parse.urlsplit(safe_url(value)).hostname or ""
        return host == domain or host.endswith("." + domain)
    except ValueError: return False

def _bing_publisher_url(value, domain):
    """Return the publisher URL carried by a Bing News RSS redirect."""
    try:
        parsed = urllib.parse.urlsplit(safe_url(value))
        if (parsed.hostname or "").lower() not in {"bing.com", "www.bing.com"}: return ""
        candidate = safe_url(urllib.parse.parse_qs(parsed.query).get("url", [""])[-1])
        return candidate if publisher_url(candidate, domain) else ""
    except (ValueError, TypeError):
        return ""


def safe_url(value):
    if not isinstance(value, str) or len(value) > 4096: return ""
    try:
        p = urllib.parse.urlsplit(value.strip())
        if p.scheme not in ("https", "http") or not p.hostname or p.username or p.password: return ""
        if any(ord(c) < 32 for c in value): return ""
        return value.strip()
    except ValueError: return ""

# WordPress/Jetpack serves feed thumbnails through resize parameters
# (GameSpot's RSS asks for ?w=300). On a TV that thumbnail is visibly soft:
# ask the same CDN for a card-sized rendition instead.
_WP_RESIZE_KEYS = {"w", "h", "resize", "fit", "crop", "quality"}


def upgrade_image_url(value, width=1600):
    if not value: return value
    try:
        parts = urllib.parse.urlsplit(value)
    except ValueError:
        return value
    if "/wp-content/uploads/" not in parts.path: return value
    query = urllib.parse.parse_qsl(parts.query, keep_blank_values=True)
    if not any(key in _WP_RESIZE_KEYS for key, _ in query): return value
    kept = [(key, item) for key, item in query if key not in _WP_RESIZE_KEYS]
    return urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(kept + [("w", str(width))])))


def text(value):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]*>", "", value or ""))).strip()[:300]

def parse_feed(data, source, feed_url, now=None):
    if len(data) > LIMIT or b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper(): raise ValueError("invalid_feed")
    # Decky's frozen Python does not ship xml.etree/pyexpat. Parse only bounded
    # RSS/Atom metadata; never evaluate entities, DTDs or article HTML.
    document = data.decode("utf-8-sig", errors="replace")
    rows = []
    now = now or time.time()
    def un_cdata(value):
        return re.sub(r"<!\[CDATA\[([\s\S]*?)\]\]>", lambda m: m.group(1), value)
    def attributes(tag):
        return {m.group(1).lower(): html.unescape(m.group(3)) for m in re.finditer(r"([\w:-]+)\s*=\s*([\"'])(.*?)\2", tag, re.S)}
    for match in re.finditer(r"<(item|entry)\b[^>]*>([\s\S]*?)</\1\s*>", document, re.I):
        item = match.group(2)
        def value(name):
            found = re.search(r"<(?:[\w-]+:)?" + re.escape(name) + r"\b[^>]*>([\s\S]*?)</(?:[\w-]+:)?" + re.escape(name) + r"\s*>", item, re.I)
            return un_cdata(found.group(1)) if found else ""
        link = html.unescape(value("link").strip())
        for tag in re.findall(r"<link\b[^>]*>", item, re.I):
            attrs = attributes(tag)
            if attrs.get("rel", "alternate") == "alternate": link = attrs.get("href") or link
        link = safe_url(link)
        title = text(value("title"))
        if not title or not link: continue
        publisher = text(value("source")) or source
        site_tag = re.search(r"<source\b[^>]*>", item, re.I)
        site_url = safe_url(attributes(site_tag.group(0)).get("url", "")) if site_tag else feed_url
        image = ""
        for tag in re.findall(r"<(?:[\w-]+:)?(?:thumbnail|content|enclosure)\b[^>]*>", item, re.I):
            attrs = attributes(tag)
            kind_match = re.match(r"<(?:(?:[\w-]+):)?([\w-]+)", tag, re.I)
            kind = kind_match.group(1).lower() if kind_match else ""
            declared_image = kind == "thumbnail" or attrs.get("medium", "").lower() == "image" or attrs.get("type", "").lower().startswith("image/")
            if declared_image:
                image = safe_url(urllib.parse.urljoin(link, attrs.get("url", "")))
                if image: break
        if not image:
            # Bing's RSS transport exposes the thumbnail as element text.
            image = safe_url(html.unescape(value("Image").strip()))
            if image and (urllib.parse.urlsplit(image).hostname or "").endswith("bing.com"):
                image = "https://" + image.split("://", 1)[-1]
        if not image:
            markup = html.unescape(value("description") or value("encoded") or value("content"))
            found = re.search(r"<img\b([^>]+)", markup, re.I)
            if found:
                attrs = attributes(found.group(1))
                raw = attrs.get("data-src") or attrs.get("data-lazy-src") or attrs.get("data-original") or attrs.get("src") or ""
                srcset = attrs.get("data-srcset") or attrs.get("data-lazy-srcset") or attrs.get("srcset") or ""
                if srcset:
                    choices = []
                    for part in srcset.split(","):
                        bits = part.strip().split()
                        if bits:
                            try: weight = float(bits[-1][:-1]) * (1000 if bits[-1].endswith("x") else 1) if len(bits) > 1 else 0
                            except ValueError: weight = 0
                            choices.append((weight, bits[0]))
                    if choices: raw = max(choices, key=lambda pair: pair[0])[1]
                image = safe_url(urllib.parse.urljoin(link, html.unescape(raw)))
        # Google News items link to a news.google.com redirector, and that page carries
        # no OpenGraph image. The publisher's own URL is in the item description, and
        # it is the only address from which a lead image can be recovered.
        article = ""
        for href in re.findall(r"<a\b[^>]+href=[\"']([^\"']+)", html.unescape(value("description")), re.I):
            candidate = safe_url(html.unescape(href))
            host = urllib.parse.urlsplit(candidate).hostname or "" if candidate else ""
            if candidate and host and not host.endswith("google.com"):
                article = candidate
                break
        published = 0
        try: published = email.utils.parsedate_to_datetime(value("pubDate")).timestamp()
        except (ValueError, TypeError, OverflowError):
            try: published = datetime.datetime.fromisoformat((value("updated") or value("date") or value("published")).replace("Z", "+00:00")).timestamp()
            except (ValueError, TypeError, OverflowError): pass
        if published and (published < now - 7 * 86400 or published > now + 86400): continue
        domain = urllib.parse.urlsplit(site_url or link).hostname or ""
        logo = "https://www.google.com/s2/favicons?" + urllib.parse.urlencode({"domain": domain, "sz": 128})
        rows.append({"title": title, "url": link, "article_url": article, "source": publisher,
                     "source_url": site_url, "image": upgrade_image_url(image), "logo": logo, "published": published})
        if len(rows) >= 60: break
    return rows

class HomeNews:
    def __init__(self, directory, fetch=None, clock=time.time, page=None):
        self.path = os.path.join(directory, "home-news.json")
        self.lock = threading.RLock()
        self.cache = {}
        self.fetch = fetch or self._fetch
        # Feeds and article pages are fetched differently; tests may override either.
        self.page = page or fetch or self._fetch_page
        self.clock = clock

    @staticmethod
    def _fetch(url):
        req = urllib.request.Request(url, headers={"User-Agent": "Playhub/1.4 RSS Reader", "Accept": "application/rss+xml, application/atom+xml, text/xml", "Accept-Encoding": "gzip"})
        with urllib.request.urlopen(req, timeout=8) as response:
            data = response.read(LIMIT + 1)
            encoded = (response.headers.get("Content-Encoding") or "").lower()
        if encoded == "gzip" or data.startswith(b"\x1f\x8b"):
            data = gzip.decompress(data)
        if len(data) > LIMIT: raise ValueError("feed_too_large")
        return data

    @staticmethod
    def _fetch_page(url):
        """Fetch an article page. Asking an HTML page for ``application/rss+xml`` is
        what made the lead image disappear for every publisher whose feed carries no
        media tag: many answer 403 or 406 to that Accept header. Only the head is
        read, and never any cookie or credential."""
        req = urllib.request.Request(url, headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Playhub/1.4 (+news card image)",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "en;q=0.8,*;q=0.5"})
        with urllib.request.urlopen(req, timeout=8) as response:
            if "html" not in (response.headers.get_content_type() or ""): raise ValueError("not_html")
            data = response.read(512 * 1024 + 1)
        if len(data) > 512 * 1024: data = data[:512 * 1024]
        return data

    def _publisher_article_url(self, value, domain):
        """Resolve a Google RSS wrapper to its publisher URL.

        The resolver mirrors the narrow Fbv4je path used by the News plugin. It
        accepts the result only when its host belongs to the allowlisted publisher;
        arbitrary links found in Google's shell are never followed.
        """
        if not _is_google_news_url(value):
            return value if publisher_url(value, domain) else ""
        legacy = _google_legacy_url(value)
        if legacy and publisher_url(legacy, domain): return legacy
        try:
            article_id = urllib.parse.urlsplit(value).path.rstrip("/").split("/")[-1]
            if not article_id: return ""
            opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
            headers = {
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/151 Safari/537.36",
                "Accept": "text/html,application/xhtml+xml,*/*;q=0.8",
                "Accept-Language": "en-US,en;q=0.8",
            }
            shells = ("https://news.google.com/articles/" + urllib.parse.quote(article_id, safe=""), value)
            for shell in shells:
                try:
                    request = urllib.request.Request(shell, headers=headers)
                    with opener.open(request, timeout=4) as response:
                        document = response.read(600 * 1024).decode("utf-8", "replace")
                    source_id, signature, timestamp = _google_decode_params(document)
                    if not signature or not timestamp: continue
                    ids = (article_id, source_id) if source_id and source_id != article_id else (article_id,)
                    for decode_id in ids:
                        inner = (
                            '["garturlreq",[["X","X",["X","X"],null,null,1,1,"US:en",null,1,null,null,null,null,null,0,1],'
                            '"X","X",1,[1,1,1],1,1,null,0,0,null,0],'
                            f'"{decode_id}",{int(timestamp)},"{signature}"]'
                        )
                        for row in (["Fbv4je", inner], ["Fbv4je", inner, None, "generic"]):
                            body = urllib.parse.urlencode({"f.req": json.dumps([[row]], separators=(",", ":"))}).encode()
                            post = urllib.request.Request(
                                "https://news.google.com/_/DotsSplashUi/data/batchexecute",
                                data=body, method="POST",
                                headers={**headers, "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
                                         "Origin": "https://news.google.com", "Referer": shell})
                            try:
                                with opener.open(post, timeout=5) as response:
                                    decoded = _google_decoded_url(response.read(1600 * 1024).decode("utf-8", "replace"))
                            except Exception:
                                continue
                            if decoded and publisher_url(decoded, domain): return decoded
                except Exception:
                    continue
        except Exception:
            pass
        return ""

    def _article_image(self, url):
        """Recover a publisher lead image using the same metadata fallbacks as News.

        Feeds are inconsistent: some expose only lazy attributes or a responsive
        ``srcset``.  Keep this bounded to the article head and choose the largest
        advertised candidate so the Home card does not fall back to a tiny logo.
        """
        try:
            html_page = self.page(url).decode("utf-8", errors="replace")[:512 * 1024]
            # Prefer OpenGraph, then Twitter cards, regardless of attribute order.
            meta_candidates = {"og:image": [], "og:image:url": [], "og:image:secure_url": [], "twitter:image": [], "twitter:image:src": []}
            for tag in re.findall(r'<meta\b[^>]+>', html_page, re.I):
                attrs = {m.group(1).lower(): html.unescape(m.group(3)) for m in re.finditer(r'([\w:-]+)\s*=\s*(["\'])(.*?)\2', tag, re.S)}
                key = (attrs.get("property") or attrs.get("name") or "").lower()
                value = attrs.get("content", "")
                if key in meta_candidates and value: meta_candidates[key].append(value)
            for key in ("og:image", "og:image:url", "og:image:secure_url", "twitter:image", "twitter:image:src"):
                for value in meta_candidates[key]:
                    image = safe_url(urllib.parse.urljoin(url, value))
                    if image: return image
            patterns = (
                r'<meta[^>]+(?:property|name)=["\'](?:og:image|og:image:url|og:image:secure_url|twitter:image|twitter:image:src)["\'][^>]+content=["\']([^"\']+)',
                r'<meta[^>]+content=["\']([^"\']+)["\'][^>]+(?:property|name)=["\'](?:og:image|og:image:url|og:image:secure_url|twitter:image|twitter:image:src)["\']',
                r'<link[^>]+rel=["\'](?:image_src|preload)["\'][^>]+href=["\']([^"\']+)',
                r'<link[^>]+(?:imagesrcset|data-srcset)=["\']([^"\']+)',
            )
            for pattern in patterns:
                match = re.search(pattern, html_page, re.I)
                if match:
                    value = html.unescape(match.group(1))
                    if "srcset" in pattern.lower():
                        candidates = []
                        for part in value.split(","):
                            bits = part.strip().split()
                            if bits:
                                try: width = int(re.sub(r"[^0-9]", "", bits[1])) if len(bits) > 1 else 0
                                except ValueError: width = 0
                                candidates.append((width, bits[0]))
                        value = max(candidates, key=lambda pair: pair[0])[1] if candidates else ""
                    image = safe_url(urllib.parse.urljoin(url, value))
                    if image: return image
            # News also accepts lazy image attributes and JSON-LD image fields.
            image_candidates = []
            for match in re.finditer(r'<img\b([^>]+)>', html_page, re.I):
                attrs = {m.group(1).lower(): html.unescape(m.group(3)) for m in re.finditer(r'([\w:-]+)\s*=\s*(["\'])(.*?)\2', match.group(1), re.S)}
                value = attrs.get("data-srcset") or attrs.get("data-lazy-srcset") or attrs.get("srcset") or attrs.get("data-lazy-src") or attrs.get("data-src") or attrs.get("data-original") or attrs.get("data-full-src") or attrs.get("src") or ""
                if "srcset" in ("data-srcset" if attrs.get("data-srcset") else "") or "srcset" in ("data-lazy-srcset" if attrs.get("data-lazy-srcset") else "") or "srcset" in ("srcset" if attrs.get("srcset") else ""):
                    choices = []
                    for part in value.split(","):
                        bits = part.strip().split()
                        if bits:
                            try: width = int(re.sub(r"[^0-9]", "", bits[1])) if len(bits) > 1 else 0
                            except ValueError: width = 0
                            choices.append((width, bits[0]))
                    value = max(choices, key=lambda pair: pair[0])[1] if choices else ""
                image = safe_url(urllib.parse.urljoin(url, value))
                if image:
                    hint = (attrs.get("class", "") + " " + attrs.get("id", "") + " " + attrs.get("alt", "")).lower()
                    score = (100 if any(word in hint for word in ("hero", "article", "cover", "featured", "og-image")) else 0)
                    try: score += int(re.sub(r"[^0-9]", "", attrs.get("width", ""))) // 10
                    except ValueError: pass
                    image_candidates.append((score, image))
            if image_candidates:
                return max(image_candidates, key=lambda pair: pair[0])[1]
            for block in re.findall(r'<script[^>]+type=["\']application/ld\+json["\'][^>]*>([\s\S]*?)</script>', html_page, re.I):
                try:
                    payload = json.loads(html.unescape(block))
                    stack = payload if isinstance(payload, list) else [payload]
                    for entry in stack:
                        if not isinstance(entry, dict): continue
                        candidate = entry.get("image")
                        if isinstance(candidate, list): candidate = candidate[0] if candidate else ""
                        if isinstance(candidate, dict): candidate = candidate.get("url", "")
                        image = safe_url(urllib.parse.urljoin(url, str(candidate or "")))
                        if image: return image
                except (ValueError, TypeError):
                    continue
        except Exception:
            pass
        return ""

    def settings(self):
        with self.lock:
            try:
                with open(self.path, encoding="utf-8") as f: raw = json.load(f)
                return {"enabled": raw.get("enabled") is True, "country": raw.get("country") if raw.get("country") in REGIONS.values() else "auto"}
            except (OSError, ValueError, AttributeError): return {"enabled": True, "country": "auto"}

    def save(self, enabled, country):
        if type(enabled) is not bool or country not in {"auto", *REGIONS.values()}: raise ValueError("invalid_news_settings")
        with self.lock:
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path + ".tmp", "w", encoding="utf-8") as f:
                json.dump({"enabled": enabled, "country": country}, f)
            os.replace(self.path + ".tmp", self.path)
            return self.settings()

    def get(self, locale):
        locale = locale if locale in REGIONS else "en"
        with self.lock:
            config = self.settings()
            if not config["enabled"]: return {"items": [], "disabled": True}
            country = REGIONS[locale] if config["country"] == "auto" else config["country"]
            key = (locale, country)
            previous = self.cache.get(key)
            now = self.clock()
            if previous and now - previous[0] < TTL: return previous[1]
            sources = SOURCES[country]
            rows, failures = [], 0
            def read(source):
                name, domain, feed = source
                def accept(data, url, syndicated=False):
                    result = parse_feed(data, name, url, now)
                    approved = []
                    for row in result:
                        bing_article = _bing_publisher_url(row["url"], domain) if syndicated == "bing" else ""
                        if bing_article:
                            row["url"] = bing_article
                            row["article_url"] = bing_article
                        if syndicated == "bing" and not row.get("image"):
                            continue
                        evidence = row["source_url"] if syndicated is True else row["url"]
                        if not publisher_url(evidence, domain): continue
                        # Keep editorial game coverage, not shopping/phone/TV promotions.
                        if re.search(r"\b(iphone|ipad|macbook|apple watch|smartphone|galaxy|lego|funko|laptop|prime video|netflix|amazon|coupon|sconto|offerte)\b", row["title"], re.I): continue
                        row["title"] = re.sub(r"\s+-\s+" + re.escape(row["source"]) + r"$", "", row["title"], flags=re.I)
                        row["source"] = name
                        row["publisher_domain"] = domain
                        # Only a syndicated item hides its publisher behind a redirector.
                        # In a publisher's own feed the description may link to something
                        # else entirely, which must never become this card's image.
                        if not syndicated: row["article_url"] = ""
                        elif not publisher_url(row.get("article_url", ""), domain): row["article_url"] = ""
                        row["logo"] = "https://www.google.com/s2/favicons?" + urllib.parse.urlencode({"domain": domain, "sz": 128})
                        approved.append(row)
                    return approved[:48]
                if feed:
                    try:
                        result = accept(self.fetch(feed), feed)
                        if result: return result
                    except Exception: pass
                # Bing's bounded RSS endpoint carries a publisher URL and thumbnail
                # in every item. It avoids dozens of per-card Google redirect
                # resolutions for publishers whose own feed is absent or blocked.
                bing = "https://www.bing.com/news/search?" + urllib.parse.urlencode({"q": "site:" + domain, "format": "rss"})
                try:
                    result = accept(self.fetch(bing), bing, syndicated="bing")
                    if result: return result
                except Exception: pass
                params = urllib.parse.urlencode({"q": "site:" + domain + " when:7d", "hl": SOURCE_LANGUAGES[country], "gl": country, "ceid": country + ":" + SOURCE_LANGUAGES[country]})
                url = "https://news.google.com/rss/search?" + params
                return accept(self.fetch(url), url, syndicated=True)
            with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
                for result in [pool.submit(read, source) for source in sources]:
                    try: rows.extend(result.result())
                    except Exception: failures += 1
            seen = set()
            seen_titles = set()
            groups = {}
            items = []
            for row in sorted(rows, key=lambda r: r["published"], reverse=True):
                title_key = row["title"].removesuffix(" - " + row["source"]).casefold()
                if row["url"] in seen or title_key in seen_titles: continue
                seen.add(row["url"]); seen_titles.add(title_key)
                groups.setdefault(row["source"].casefold(), []).append(row)
            # Keep multiple outlets visible instead of letting one prolific feed fill the shelf.
            while groups and len(items) < 48:
                for publisher in list(groups):
                    items.append(groups[publisher].pop(0))
                    if not groups[publisher]: del groups[publisher]
                    if len(items) == 48: break
            # Publisher feeds vary widely: recover missing lead images from the
            # article's OpenGraph metadata, without replacing an image supplied
            # by the feed. Keep the enrichment bounded and parallel.
            missing = [item for item in items if not item.get("image")][:48]
            if missing:
                def find_image(item):
                    target = item.get("article_url") or item["url"]
                    if _is_google_news_url(target):
                        target = self._publisher_article_url(target, item.get("publisher_domain", ""))
                        if target: item["article_url"] = target
                    return self._article_image(target) if target else ""
                with concurrent.futures.ThreadPoolExecutor(max_workers=6) as image_pool:
                    futures = {image_pool.submit(find_image, item): item for item in missing}
                    for future in concurrent.futures.as_completed(futures):
                        item = futures[future]
                        try: item["image"] = future.result() or item.get("image", "")
                        except Exception: pass
            stale = not items and previous is not None
            data = {"items": items[:48] if items else (previous[1]["items"] if stale else []), "updated": previous[1]["updated"] if stale else now, "stale": stale, "partial": failures > 0}
            self.cache[key] = (now, data)
            return data
