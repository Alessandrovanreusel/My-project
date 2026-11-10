import sys, glob, os
from PIL import Image, ImageDraw
pattern, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 4
fs = sorted(glob.glob(pattern))
W, H = 480, 270
rows = (len(fs) + cols - 1) // cols
sheet = Image.new('RGB', (W * cols, H * rows), 'black')
for i, f in enumerate(fs):
    im = Image.open(f).convert('RGB').resize((W, H))
    d = ImageDraw.Draw(im); d.rectangle([0, 0, W, 18], fill='black'); d.text((4, 3), os.path.basename(f), fill='yellow')
    sheet.paste(im, ((i % cols) * W, (i // cols) * H))
sheet.save(out); print(len(fs), 'frames ->', out)
