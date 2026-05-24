# wz-ui-dump

Prints a WZ node subtree with each canvas's **dimensions** and **origin** — the
asset-side "origin detector". Combined with the placement coordinates the client
code uses for a screen, the origin determines where every widget lands on screen.

## Usage

```powershell
# Resolve UI.wz against MAPLECLAUDE_WZ_DIR:
$env:MAPLECLAUDE_WZ_DIR = 'X:\path\to\maplestory'
dotnet run --project tools/wz-ui-dump -- UI.wz Login.img/WorldSelect

# Or pass an absolute .wz path:
dotnet run --project tools/wz-ui-dump -- X:\ms\UI.wz Login.img/Common --depth 3

# Only canvases (skip container chatter):
dotnet run --project tools/wz-ui-dump -- UI.wz Login.img/WorldSelect --canvas-only
```

Arguments: `<wzFileOrName> <nodePath> [--depth N] [--canvas-only]`.

## Example output

```
Login.img/WorldSelect
  chBackgrn  [Canvas 371x222 origin=(0, 0)]
  BtWorld  (Property)
    0  (Property)
      normal
        0  [Canvas 81x24 origin=(0, 0)]
  channel  (Property)
    0  (Property)
      normal  [Canvas 60x21 origin=(0, 0)]
```

WZ files are copyrighted and machine-local; none are committed (`*.wz` is
gitignored). Point the tool at your own asset directory.
