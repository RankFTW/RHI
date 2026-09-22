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

# Test GameData table for Lies of P
r = s.get('https://www.pcgamingwiki.com/w/api.php', params={
    'action': 'cargoquery',
    'tables': 'Game,GameData',
    'fields': 'Game._pageName=Page,GameData.Type,GameData.Platform,GameData.Paths',
    'join_on': 'Game._pageID=GameData._pageID',
    'where':   'Game._pageName="Lies of P"',
    'format':  'json'
})
print("GameData for Lies of P:")
print(json.dumps(r.json(), indent=2))
time.sleep(1)

# Test API table
r = s.get('https://www.pcgamingwiki.com/w/api.php', params={
    'action': 'cargoquery',
    'tables': 'Game,API',
    'fields': 'Game._pageName=Page,Game.Steam_AppID,API.Direct3D_versions,API.Vulkan_versions,API.OpenGL_versions',
    'join_on': 'Game._pageID=API._pageID',
    'where':   'Game._pageName="Lies of P"',
    'format':  'json'
})
print("API for Lies of P:")
print(json.dumps(r.json(), indent=2))
