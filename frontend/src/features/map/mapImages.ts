/** Converts a straight-alpha, top-down C# RGBA frame without changing source colors. */
export function rgbaFrameImage(rgba: string, width: number, height: number): string {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0 || width * height > 2_097_152) {
    throw new Error('服务端返回的地图图像尺寸无效。');
  }
  const decoded = atob(rgba);
  if (decoded.length !== width * height * 4) throw new Error('服务端返回的地图像素长度无效。');
  const pixels = new Uint8ClampedArray(decoded.length);
  for (let index = 0; index < decoded.length; index++) pixels[index] = decoded.charCodeAt(index);
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('浏览器无法创建地图图像。');
  context.putImageData(new ImageData(pixels, width, height), 0, 0);
  return canvas.toDataURL('image/png');
}
