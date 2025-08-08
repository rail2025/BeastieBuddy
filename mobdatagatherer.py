import asyncio
import json
import random
import aiohttp
from bs4 import BeautifulSoup

BASE_URL = "https://ffxiv.consolegameswiki.com"
HEADERS = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36"}
CANONICAL_SOURCE_URL = f"{BASE_URL}/wiki/Category:Enemies"

class DataIntegrityValidator:
    async def _fetch_all_canonical_urls(self, session: aiohttp.ClientSession) -> list[str]:
        urls = []
        next_page_url = CANONICAL_SOURCE_URL

        while next_page_url:
            try:
                html = await fetch_html(session, next_page_url)
                if not html:
                    next_page_url = None
                    continue

                soup = BeautifulSoup(html, 'lxml')
                urls.extend([BASE_URL + a['href'] for a in soup.select('div.mw-category a') if a.get('href', '').startswith('/wiki/')])
                
                next_page_link = soup.find('a', string='next page')
                if next_page_link and next_page_link.get('href'):
                    next_page_url = BASE_URL + next_page_link['href']
                else:
                    next_page_url = None
            except Exception as e:
                print(f"    [Validator] Error fetching a canonical page: {e}")
                next_page_url = None
        return urls

    async def is_valid_target(self, session: aiohttp.ClientSession, url_to_check: str) -> bool:
        print(f"    [Validator] Building canonical list to verify {url_to_check.split('/')[-1]}...")
        canonical_list = await self._fetch_all_canonical_urls(session)
        return url_to_check in canonical_list

async def fetch_html(session: aiohttp.ClientSession, url: str) -> str | None:
    try:
        delay = random.uniform(1, 5)
        print(f"    [Rate Limiter] Waiting for {delay:.2f} seconds before fetching {url.split('/')[-1]}...")
        await asyncio.sleep(delay)
        
        async with session.get(url, headers=HEADERS, timeout=30) as response:
            response.raise_for_status()
            return await response.text()
    except Exception as e:
        print(f"  [!] Failed to fetch {url.split('/')[-1]}: {e}")
        return None

async def parse_mob_data(html: str) -> dict | None:
    if not html: return None
    try:
        soup = BeautifulSoup(html, 'lxml')
        mob_name = soup.select_one('h1#firstHeading').get_text(strip=True)
        
        location = {}
        zone_dt = soup.find('dt', string='Zone')
        if zone_dt and (zone_dd := zone_dt.find_next_sibling('dd')):
            location['zone'] = zone_dd.find('a').get_text(strip=True) if zone_dd.find('a') else "Unknown"
            coords_tag = zone_dd.find('small')
            location['coords'] = coords_tag.get_text(strip=True).strip('()') if coords_tag else None

        return {"mob_name": mob_name, "location": location}
    except Exception as e:
        print(f"  [!] Failed to parse data: {e}")
        return None

async def process_single_url(session: aiohttp.ClientSession, url: str, validator: DataIntegrityValidator):
    if not await validator.is_valid_target(session, url):
        print(f"  [!] Target {url.split('/')[-1]} failed validation, skipping.")
        return None

    html = await fetch_html(session, url)
    return await asyncio.to_thread(parse_mob_data, html)

async def main():
    patch = input("Enter patch number (e.g., 6.0): ").strip()
    category_url = f"{BASE_URL}/wiki/Category:Patch_{patch.replace('.', '_')}_Enemies"
    
    validator = DataIntegrityValidator()

    async with aiohttp.ClientSession() as session:
        initial_html = await fetch_html(session, category_url)
        if not initial_html: 
            print("Could not fetch the main category page. Exiting.")
            return

        soup = BeautifulSoup(initial_html, 'lxml')
        mob_urls = [BASE_URL + a['href'] for a in soup.select('div.mw-category a') if a.get('href', '').startswith('/wiki/')]
        
        tasks = [process_single_url(session, url, validator) for url in mob_urls]
        
        print(f"\nProcessing {len(tasks)} mob pages with deep validation and rate limiting...")
        results = await asyncio.gather(*tasks)
        
        all_mob_data = [res for res in results if res]

    filename = f"ffxiv_patch_{patch.replace('.', '_')}_mobs.json"
    with open(filename, 'w', encoding='utf-8') as f:
        json.dump(all_mob_data, f, indent=2)

    print(f"\nScraping complete. Data for {len(all_mob_data)} mobs saved to {filename}")

if __name__ == "__main__":
    asyncio.run(main())