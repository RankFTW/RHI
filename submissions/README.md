# RHI Game Data Submissions

Drop user-submitted `RHI_GameData_*.zip` files here, then run:

```
python docs/merge_game_db.py --submissions submissions/ --output .
```

This produces:
- `game_db.json` — the merged database (push this to GitHub)
- `conflicts.json` — fields where submissions disagree (review manually before pushing)

## Workflow

1. User clicks **Export Game Data** in RHI Settings
2. They paste the zip into Discord
3. Download it and drop it in this folder
4. Run the merge script
5. Review `conflicts.json` for anything that needs a manual decision
6. Push `game_db.json` to the repo alongside `manifest.json`
