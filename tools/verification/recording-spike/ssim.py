"""Frame comparison helpers for the 1.14 spike: SSIM (Gaussian-free 8x8 box SSIM on luma), PSNR, mean colour."""
import sys, numpy as np
from PIL import Image
def load(p, size=None):
    im = Image.open(p).convert('RGB')
    if size and im.size != size: im = im.resize(size, Image.BILINEAR)
    return np.asarray(im).astype(np.float64)
def box(a, k=8):
    c = np.cumsum(np.cumsum(np.pad(a, ((1,0),(1,0))), 0), 1)
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k*k)
def ssim(a, b):
    ya = a @ [0.299, 0.587, 0.114]; yb = b @ [0.299, 0.587, 0.114]
    C1, C2 = (0.01*255)**2, (0.03*255)**2
    ma, mb = box(ya), box(yb)
    va = box(ya*ya) - ma*ma; vb = box(yb*yb) - mb*mb; cov = box(ya*yb) - ma*mb
    s = ((2*ma*mb + C1)*(2*cov + C2)) / ((ma*ma + mb*mb + C1)*(va + vb + C2))
    return float(s.mean())
def psnr(a, b):
    mse = np.mean((a-b)**2); return float('inf') if mse == 0 else float(10*np.log10(255**2/mse))
if __name__ == '__main__':
    a = load(sys.argv[1]); b = load(sys.argv[2], (a.shape[1], a.shape[0]))
    print(f"SSIM={ssim(a,b):.4f} PSNR={psnr(a,b):.2f}dB meanA={a.mean((0,1)).round(1)} meanB={b.mean((0,1)).round(1)} meanAbsDiff={np.abs(a-b).mean():.2f} stdA={a.std():.1f} flippedSSIM={ssim(a,b[::-1]):.4f}")
