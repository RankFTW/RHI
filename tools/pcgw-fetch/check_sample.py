import json
data = json.loads(open('g:/RDXC/database/pcgw_data.json', encoding='utf-8').read())
games = data['games']
for name in ['Lies of P', 'Cyberpunk 2077', 'Elden Ring', 'Halo Infinite', 'Overwatch 2', 'SILENT HILL 2']:
    entry = games.get(name, 'NOT FOUND')
    print(f"{name}: {entry}")
print()
print(f"Total: {data['game_count']:,} games, generated: {data['generated']}")
print(f"File size: {len(json.dumps(data))//1024:,} KB")
# Games with config paths
with_config = sum(1 for e in games.values() if 'config_path' in e or 'config_path_xbox' in e)
print(f"Games with config paths: {with_config:,}")
# Games with DX12
with_dx12 = sum(1 for e in games.values() if e.get('dx12'))
print(f"Games with DX12: {with_dx12:,}")
