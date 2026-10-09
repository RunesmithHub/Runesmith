# Branding

Runesmith's logo is `src/Runesmith.App/Assets/runesmith.webp`, a 500 by 491 pixel image with a transparent background. Every other logo
file is made from it, centered on a square transparent canvas and never scaled up past 512 pixels.

| File | Sizes | Used for |
| --- | --- | --- |
| `src/Runesmith.App/Assets/runesmith.ico` | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 | The Windows executable's icon |
| `src/Runesmith.Shell/Assets/runesmith.png` | 256 | The window icon on every system, the welcome screen and the About dialog |
| `packaging/linux/share/icons/hicolor/<size>/apps/runesmith.png` | 16, 24, 32, 48, 64, 128, 256, 512 | The Linux menu entry and task bars |
| `packaging/linux/share/applications/runesmith.desktop` | | The Linux menu entry; published Linux builds carry `share/` |
| `website/static/img/favicon.ico`, `logo.png`, `apple-touch-icon.png` | 16 to 48, 64, 180 | The documentation site |
| `icon.png` in each official plugin's repository | 256 | The plugin's icon, as its `plugin.json` names it; copy `src/Runesmith.Shell/Assets/runesmith.png` there |

To make them again after the logo changes, run from the repository root:

```bash
nix shell nixpkgs#imagemagick -c bash -c '
square=$(mktemp --suffix .png)
magick src/Runesmith.App/Assets/runesmith.webp -background none -gravity center -extent 500x500 "$square"
size() { magick "$square" -filter Lanczos -resize "$1x$1" -strip "PNG32:$2"; }
ico=(); for s in 16 20 24 32 40 48 64 96 128 256; do size $s /tmp/runesmith-$s.png; ico+=(/tmp/runesmith-$s.png); done
magick "${ico[@]}" src/Runesmith.App/Assets/runesmith.ico
size 256 src/Runesmith.Shell/Assets/runesmith.png
for s in 16 24 32 48 64 128 256 512; do size $s packaging/linux/share/icons/hicolor/${s}x${s}/apps/runesmith.png; done
magick /tmp/runesmith-16.png /tmp/runesmith-32.png /tmp/runesmith-48.png website/static/img/favicon.ico
size 180 website/static/img/apple-touch-icon.png
size 64 website/static/img/logo.png
'
```
