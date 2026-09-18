"""North Carolina (NCREC) public licensing database crawler with a resumable page cache.

One crawler per state; this one covers the NC Real Estate Commission. Modes:
  charlotte (default)  Rebuild the Charlotte 65-firm roster capture from the
                       link map (firm details, BIC license details, and every
                       "Licensees under this Broker-In-Charge" roster page).
  --lic-range A B      Sweep license detail pages for every license number in
                       [A, B] — statewide enumeration without the search UI.
  --new                Incremental: probe forward from the highest license
                       number in existing captures until --gap-tolerance
                       consecutive vacant numbers; captures only newly issued
                       licenses (cached vacancies are re-checked fresh).

Every page fetched is cached under tools/.ncrec-cache/, so interrupted runs
resume where they left off and repeat runs only fetch pages older than
--cache-max-age-days (default 30). Delete the cache dir to force a full refetch.

Usage (from src/):
  python tools/nc_ncrec_capture.py
  python tools/nc_ncrec_capture.py --lic-range 100000 105000 --workers 4
  python tools/nc_ncrec_capture.py --new
"""
import argparse
import hashlib
import json
import re
import time
import urllib.request
import html as htmlmod
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

BASE = 'https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_WRAPPER?ID='
ROSTER_DIR = Path(__file__).resolve().parent.parent / 'docs' / 'NORTH CAROLINA' / 'LICENSING' / 'TEXTs' / 'BROKER ROSTER'
MAP_PATH = ROSTER_DIR / 'Charlotte - 65 Firms - NCREC Link Map 2026-09-17.json'
OUT_PATH = ROSTER_DIR / 'Charlotte - 65 Firms - NCREC Roster Capture 2026-09-17.json'
CACHE_DIR = Path(__file__).resolve().parent / '.ncrec-cache'
DELAY_SECONDS = 0.1
CACHE_MAX_AGE_DAYS = 30


def cache_path(url):
    return CACHE_DIR / (hashlib.sha1(url.encode()).hexdigest() + '.html')


def get(url, tries=3, force=False):
    cache_file = cache_path(url)
    if not force and cache_file.exists() and (time.time() - cache_file.stat().st_mtime) < CACHE_MAX_AGE_DAYS * 86400:
        return cache_file.read_text(encoding='utf-8')
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            raw = urllib.request.urlopen(req, timeout=30).read().decode('utf-8', 'replace')
            CACHE_DIR.mkdir(exist_ok=True)
            cache_file.write_text(raw, encoding='utf-8')
            time.sleep(DELAY_SECONDS)
            return raw
        except Exception:
            if attempt == tries - 1:
                raise
            time.sleep(1)


def clean(raw):
    text = re.sub(r'(?s)<script.*?</script>', ' ', raw)
    text = re.sub(r'<[^>]+>', ' ', text)
    return re.sub(r'\s+', ' ', htmlmod.unescape(text))


def grab(text, pattern):
    match = re.search(pattern, text)
    return match.group(1).strip() if match else None


def crawl_firms(mapdata):
    firms = []
    for firm in mapdata['firms']:
        text = clean(get(BASE + 'O4W_FIRM_SHOW_DETAIL_NEW&CID=' + firm['c']))
        blob = grab(text, r'Firm Search Results\s+(.*?)\s+License:\s*C\d+') or ''
        address = blob[len(firm['n']):].strip() if blob.upper().startswith(firm['n'].upper()) else blob
        firms.append({
            'licenseNumber': firm['c'],
            'name': firm['n'],
            'status': grab(text, r'License:\s*' + firm['c'] + r'\s*Status:\s*([A-Z][A-Z\- ]*?)\s*(?:Qualifying|New Search|Location)'),
            'qualifyingBroker': grab(text, r'Qualifying Broker:\s*(.+?)\s*New Search'),
            'address': address or None,
        })
    return firms


def parse_license_detail(lic_no, raw, fallback_name=None):
    text = clean(raw)
    if 'Issue Date:' not in text:
        return None
    primary = re.search(r'Primary Firm:\s*(C\d+)\s+(.+?)\s+(?:Firm may|Broker-In-Charge|New Search)', text)
    return {
        'licenseNumber': lic_no,
        'name': grab(text, r'Name:\s*(.+?)\s*Type:') or fallback_name,
        'issueDate': grab(text, r'Issue Date:\s*(\d{2}/\d{2}/\d{4})'),
        'type': grab(text, r'Type:\s*([A-Z ]+?)\s*Level:'),
        'level': grab(text, r'Level:\s*([A-Z]+)\s*Status:'),
        'status': grab(text, r'Status:\s*([A-Z][A-Z\- ]*?)\s*(?:Primary Firm:|Broker-In-Charge:|New Search)'),
        'primaryFirm': primary.group(1) if primary else None,
        'primaryFirmName': primary.group(2).strip() if primary else None,
        'isBrokerInCharge': grab(text, r'Broker-In-Charge:\s*(Yes|No)') == 'Yes',
        'bicEligible': grab(text, r'BIC Eligible:\s*(Yes|No)') == 'Yes',
        'hasSupervisedRoster': 'O4W_LIC_SLSM_LIST_NEW' in raw,
    }


def crawl_bic_details(mapdata):
    seen, licensees, affiliations = set(), [], []
    for firm in mapdata['firms']:
        for link in mapdata['map'][firm['c']]:
            affiliations.append({'firm': firm['c'], 'licensee': link['o'], 'isBrokerInCharge': True, 'source': 'firm-page-bic-link'})
            if link['o'] in seen:
                continue
            seen.add(link['o'])
            raw = get(BASE + 'O4W_LIC_SHOW_DETAIL_NEW&LIC_NO=' + link['o'])
            record = parse_license_detail(link['o'], raw, fallback_name=link['n'])
            if record:
                record['capturedVia'] = 'firm-bic-link'
                licensees.append(record)
    return licensees, affiliations


def crawl_supervised_rosters(licensees):
    rosters = []
    for licensee in licensees:
        if not licensee.get('hasSupervisedRoster'):
            continue
        raw = get(BASE + 'O4W_LIC_SLSM_LIST_NEW&LIC_NO=' + licensee['licenseNumber'])
        rows, current_firm = [], None
        for tr in re.findall(r'(?s)<tr[^>]*>(.*?)</tr>', raw):
            cells = [re.sub(r'\s+', ' ', htmlmod.unescape(re.sub(r'<[^>]+>', ' ', cell))).strip()
                     for cell in re.findall(r'(?s)<td[^>]*>(.*?)</td>', tr)]
            if not cells:
                continue
            nonempty = [cell for cell in cells if cell]
            if (len(nonempty) == 1 and not re.match(r'^\d{5,7}$', nonempty[0])
                    and ',' not in nonempty[0] and not re.match(r'\d{2}/\d{2}/\d{4}', nonempty[0])):
                current_firm = nonempty[0]
                continue
            lic_match = re.search(r'LIC_NO=(\d+)', tr)
            if not lic_match:
                continue
            renew = next((cell for cell in cells if re.match(r'^\d{2}/\d{2}/\d{4}$', cell)), None)
            hours = [cell for cell in (cells[cells.index(renew) + 1:] if renew in cells else [])
                     if re.match(r'^\d+(\.\d+)?$', cell)] if renew else []
            rows.append({
                'licenseNumber': lic_match.group(1),
                'name': next((cell for cell in cells if ',' in cell and any(ch.isalpha() for ch in cell)), None),
                'level': next((cell for cell in cells if cell in ('B', 'PB', 'BL', 'FB')), None),
                'renewDate': renew,
                'updateHours': hours[0] if len(hours) > 0 else None,
                'electiveHours': hours[1] if len(hours) > 1 else None,
                'plHours': hours[2] if len(hours) > 2 else None,
                'firmName': current_firm,
            })
        rosters.append({'bicLicenseNumber': licensee['licenseNumber'], 'bicName': licensee['name'], 'rows': rows})
    return rosters


def crawl_license_range(start, end, workers=1):
    def fetch_one(number):
        lic_no = str(number)
        return parse_license_detail(lic_no, get(BASE + 'O4W_LIC_SHOW_DETAIL_NEW&LIC_NO=' + lic_no))

    numbers = range(start, end + 1)
    records = []
    with ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
        for index, record in enumerate(pool.map(fetch_one, numbers), 1):
            if record:
                records.append(record)
            if index % 500 == 0:
                print(f'  scanned {index}/{len(numbers)} — {len(records)} licenses found')
    return records


def run_license_range(start, end, workers):
    records = crawl_license_range(start, end, workers)
    out_path = ROSTER_DIR / f'NCREC License Range {start}-{end} Capture.json'
    capture = {
        'source': 'North Carolina Real Estate Commission public licensing database',
        'searchUrl': BASE + 'O4W_LIC_SHOW_DETAIL_NEW',
        'query': {'licenseRangeStart': start, 'licenseRangeEnd': end},
        'retrievedAtUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'parserVersion': 'ncrec-lic-range-v1',
        'stats': {'scanned': end - start + 1, 'licenseeCount': len(records)},
        'licensees': records,
    }
    out_path.write_text(json.dumps(capture, indent=1), encoding='utf-8')
    print(f'RANGE CAPTURE COMPLETE scanned={end - start + 1} found={len(records)} -> {out_path.name}')


def find_known_max():
    highest = 0
    for path in ROSTER_DIR.glob('*.json'):
        try:
            data = json.loads(path.read_text(encoding='utf-8-sig'))
        except (json.JSONDecodeError, OSError):
            continue
        numbers = [entry.get('licenseNumber') for entry in data.get('licensees', [])]
        numbers += [row.get('licenseNumber') for roster in data.get('supervisedRosters', []) for row in roster.get('rows', [])]
        numbers += [entry.get('o') for links in data.get('map', {}).values() for entry in links]
        for value in numbers:
            if value and str(value).isdigit():
                highest = max(highest, int(value))
    return highest


def run_whats_new(gap_tolerance, max_scan=50000):
    start = find_known_max() + 1
    if start == 1:
        raise SystemExit('No existing captures found to determine the highest known license number.')
    records, misses, number = [], 0, start
    while misses < gap_tolerance and number < start + max_scan:
        url = BASE + 'O4W_LIC_SHOW_DETAIL_NEW&LIC_NO=' + str(number)
        raw = get(url)
        record = parse_license_detail(str(number), raw)
        if record is None and (time.time() - cache_path(url).stat().st_mtime) > 300:
            # stale cached vacancy: the number may have been issued since — re-check fresh
            record = parse_license_detail(str(number), get(url, force=True))
        if record:
            records.append(record)
            misses = 0
        else:
            misses += 1
        number += 1
    scanned = number - start
    out_path = ROSTER_DIR / f'NCREC New Licenses From {start} Capture.json'
    capture = {
        'source': 'North Carolina Real Estate Commission public licensing database',
        'searchUrl': BASE + 'O4W_LIC_SHOW_DETAIL_NEW',
        'query': {'newSinceLicenseNumber': start - 1, 'gapTolerance': gap_tolerance},
        'retrievedAtUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'parserVersion': 'ncrec-whats-new-v1',
        'stats': {'scanned': scanned, 'licenseeCount': len(records)},
        'licensees': records,
    }
    out_path.write_text(json.dumps(capture, indent=1), encoding='utf-8')
    print(f'WHATS-NEW COMPLETE from={start} scanned={scanned} newLicenses={len(records)} -> {out_path.name}')


def run_charlotte():
    mapdata = json.loads(MAP_PATH.read_text(encoding='utf-8-sig'))
    firms = crawl_firms(mapdata)
    licensees, affiliations = crawl_bic_details(mapdata)
    rosters = crawl_supervised_rosters(licensees)
    total_rows = sum(len(roster['rows']) for roster in rosters)
    capture = {
        'source': mapdata['source'],
        'searchUrl': mapdata['searchUrl'],
        'query': mapdata['query'],
        'retrievedAtUtc': mapdata['retrievedAtUtc'],
        'parserVersion': 'ncrec-charlotte65-v1',
        'stage': 'B',
        'stats': {
            'firmCount': len(firms),
            'licenseeCount': len(licensees),
            'affiliationCount': len(affiliations),
            'supervisedRosterCount': len(rosters),
            'supervisedLicenseeRows': total_rows,
        },
        'firms': firms,
        'licensees': licensees,
        'affiliations': affiliations,
        'supervisedRosters': rosters,
    }
    OUT_PATH.write_text(json.dumps(capture, indent=1), encoding='utf-8')
    print(f'CAPTURE COMPLETE firms={len(firms)} bics={len(licensees)} rosters={len(rosters)} supervisedRows={total_rows}')


def main():
    global CACHE_MAX_AGE_DAYS
    parser = argparse.ArgumentParser(description='NC (NCREC) crawler with resumable page cache.')
    parser.add_argument('--lic-range', nargs=2, type=int, metavar=('START', 'END'), help='sweep license numbers START..END instead of the Charlotte map')
    parser.add_argument('--new', action='store_true', help='incremental scan for licenses issued after the highest known number')
    parser.add_argument('--gap-tolerance', type=int, default=300, help='consecutive vacant numbers before --new stops (default 300)')
    parser.add_argument('--workers', type=int, default=1, help='parallel fetchers for --lic-range (keep small; default 1)')
    parser.add_argument('--cache-max-age-days', type=int, default=CACHE_MAX_AGE_DAYS, help='reuse cached pages younger than this (default 30)')
    args = parser.parse_args()
    CACHE_MAX_AGE_DAYS = args.cache_max_age_days
    if args.new:
        run_whats_new(args.gap_tolerance)
    elif args.lic_range:
        run_license_range(args.lic_range[0], args.lic_range[1], args.workers)
    else:
        run_charlotte()


if __name__ == '__main__':
    main()
