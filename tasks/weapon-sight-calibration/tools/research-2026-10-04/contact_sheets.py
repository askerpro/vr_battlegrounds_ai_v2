import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).parent / 'visual-review'
inventory = json.loads((root / 'inventory.json').read_text(encoding='utf-8'))
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 23)
for key, columns in [('weapons', 4), ('donors', 4)]:
    models = inventory[key]
    width, height = 512, 306
    sheet = Image.new('RGB', (columns*width, ((len(models)+columns-1)//columns)*height), '#202630')
    draw = ImageDraw.Draw(sheet)
    for i, model in enumerate(models):
        x, y = (i % columns)*width, (i//columns)*height
        with Image.open(root / model['id'] / 'side.png') as source:
            source.thumbnail((width, height-38))
            sheet.paste(source, (x, y+38))
        draw.text((x+12, y+6), model['id'], fill='#ffffff', font=font)
    path = root / f'{key}-overview.jpg'
    sheet.save(path, quality=94)
    print(path)
