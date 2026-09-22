import requests, json, time

s = requests.Session()
s.headers['User-Agent'] = 'RHI-PCGW-Fetcher/1.0 (github.com/RankFTW/RHI; rankftw@googlemail.com)'
r = s.get('https://www.pcgamingwiki.com/w/api.php', params={'action':'query','meta':'tokens','type':'login','format':'json'})
token = r.json()['query']['tokens']['logintoken']
s.post('https://www.pcgamingwiki.com/w/api.php', data={
    'action':'login','lgname':'Rankftw@Fiatbrava14!',
    'lgpassword':'9ceo2f645o0f8dbq0elf2r03knrbglfs',
    'lgtoken':token,'format':'json'
})
time.sleep(1)

# Check what platform values exist for config type
r = s.get('https://www.pcgamingwiki.com/w/api.php', params={
    'action': 'cargoquery',
    'tables': 'GameData',
    'fields': 'GameData.Platform=Platform,COUNT(*)=n',
    'where':  "GameData.Type='Config'",
    'group_by': 'GameData.Platform',
    'order_by': 'n DESC',
    'limit':  '30',
    'format': 'json'
})
print("Platform values for Config entries:")
for item in r.json().get('cargoquery', []):
    print(f"  {item['title']['Platform']:30s} {item['title']['n']}")

time.sleep(1)

# Check Of Ash and Steel specifically
r2 = s.get('https://www.pcgamingwiki.com/w/api.php', params={
    'action': 'cargoquery',
    'tables': 'Game,GameData',
    'fields': 'Game._pageName=Page,GameData.Type,GameData.Platform,GameData.Paths',
    'join_on': 'Game._pageID=GameData._pageID',
    'where':  'Game._pageName="Of Ash and Steel"',
    'format': 'json'
})
print("\nOf Ash and Steel GameData:")
print(json.dumps(r2.json(), indent=2))
