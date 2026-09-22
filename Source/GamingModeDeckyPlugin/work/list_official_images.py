import re
import sys
from urllib.request import urlopen
from urllib.parse import urljoin
sys.stdout.reconfigure(encoding="utf-8")

for url in [
    "https://www.konami.com/mg/archive/mgs_tlc/",
    "https://www.konami.com/mg/archive/mgs/about_mgs/index.html",
    "https://www.konami.com/games/eu/en/products/mgsv_tde/",
]:
    print("\n", url)
    html = urlopen(url, timeout=30).read().decode("utf-8", "ignore")
    for tag in re.findall(r"<img\b[^>]*>", html, re.I):
        match = re.search(r"(?:src|data-src)=[\"']([^\"']+)", tag, re.I)
        if match:
            print(urljoin(url, match.group(1)), tag)
