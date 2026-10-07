using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AgentVitals;

/// <summary>Кадры значка в трее: кардиограмма цвета квот, бег линии и синяя точка — сессии.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class TrayGlyph
{
	private const int _frames = 8;

	// Кардиограмма на сетке 32×32 — та же, что у иконки приложения.
	private static readonly Point[] _pulse =
		[new(2, 18), new(9, 18), new(12, 10), new(16, 26), new(20, 5), new(23, 18), new(30, 18)];

	// Кадров немного — по цвету, теме панели, номеру кадра и размеру: держим их все.
	private static readonly Dictionary<(Health Health, bool Dark, int Frame, int Size), IntPtr> _cache = [];

	/// <summary>HICON кадра; кадры кэшируются и живут до конца процесса.</summary>
	public static IntPtr Get(Health health, SessionState live, int tick, bool dark, int size)
	{
		var frame = live switch
		{
			SessionState.Idle => -1,
			SessionState.Working => tick % _frames,
			SessionState.Waiting => _frames + tick / 4 % 2,
			_ => throw new NotImplementedException("SessionState switch error"),
		};

		var key = (health, dark, frame, size);
		if (!_cache.TryGetValue(key, out var icon))
		{
			icon = Draw(health, dark, frame, size);
			_cache[key] = icon;
		}

		return icon;
	}

	private static IntPtr Draw(Health health, bool dark, int frame, int size)
	{
		var scale = size / 32.0;
		var tint = Tint(health, dark);
		var thickness = Math.Max(2, 3.4 * scale);
		var line = Line(scale);

		using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
		using (var context = bitmap.CreateDrawingContext())
		{
			if (frame is >= 0 and < _frames)
			{
				// Работает: линия приглушена, по ней бежит яркий участок.
				context.DrawGeometry(null, Pen(tint, 0.35, thickness), line);

				using (context.PushClip(new Rect((frame * 32.0 / _frames - 6) * scale, 0, 12 * scale, size)))
				{
					context.DrawGeometry(null, Pen(tint, 1, thickness), line);
				}
			}
			else
			{
				context.DrawGeometry(null, Pen(tint, 1, thickness), line);
			}

			// Ждёт разрешения: синяя точка в углу, в нечётных кадрах гаснет — мигает.
			if (frame == _frames)
			{
				var blue = Color.Parse(dark ? "#0A84FF" : "#007AFF");
				context.DrawEllipse(new SolidColorBrush(blue), null, new Point(25.5 * scale, 25.5 * scale), 5.5 * scale, 5.5 * scale);
			}
		}

		return ToIcon(bitmap, size);
	}

	private static Color Tint(Health health, bool dark) => Color.Parse((health, dark) switch
	{
		(Health.Good, true) => "#30D158",
		(Health.Good, false) => "#248A3D",
		(Health.Warning, true) => "#FF9F0A",
		(Health.Warning, false) => "#B25000",
		(Health.Critical, true) => "#FF453A",
		(Health.Critical, false) => "#C7302B",
		(_, true) => "#9B9BA1",
		(_, false) => "#6E6E76",
	});

	private static Pen Pen(Color color, double opacity, double thickness) =>
		new(new SolidColorBrush(color, opacity), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

	private static StreamGeometry Line(double scale)
	{
		var geometry = new StreamGeometry();

		using (var figure = geometry.Open())
		{
			figure.BeginFigure(_pulse[0] * scale, isFilled: false);

			foreach (var point in _pulse.Skip(1))
			{
				figure.LineTo(point * scale);
			}

			figure.EndFigure(isClosed: false);
		}

		return geometry;
	}

	// Skia отдаёт точки с premultiplied-альфой, а значки Windows ждут обычную.
	private static IntPtr ToIcon(RenderTargetBitmap bitmap, int size)
	{
		var pixels = new byte[size * size * 4];

		fixed (byte* buffer = pixels)
		{
			bitmap.CopyPixels(new PixelRect(0, 0, size, size), (IntPtr)buffer, pixels.Length, size * 4);
		}

		for (var i = 0; i < pixels.Length; i += 4)
		{
			var alpha = pixels[i + 3];

			if (alpha is > 0 and < 255)
			{
				pixels[i] = (byte)(pixels[i] * 255 / alpha);
				pixels[i + 1] = (byte)(pixels[i + 1] * 255 / alpha);
				pixels[i + 2] = (byte)(pixels[i + 2] * 255 / alpha);
			}
		}

		var header = new BITMAPINFOHEADER
		{
			biSize = 40,
			biWidth = size,
			biHeight = -size,
			biPlanes = 1,
			biBitCount = 32,
		};

		var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
		Marshal.Copy(pixels, 0, bits, pixels.Length);

		// Маска пустая: прозрачность — в альфа-канале самих точек.
		var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
		var info = new ICONINFO
		{
			fIcon = 1,
			hbmMask = mask,
			hbmColor = color,
		};

		var icon = CreateIconIndirect(ref info);
		DeleteObject(color);
		DeleteObject(mask);

		return icon;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BITMAPINFOHEADER
	{
		public uint biSize;
		public int biWidth;
		public int biHeight;
		public ushort biPlanes;
		public ushort biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ICONINFO
	{
		public int fIcon;
		public int xHotspot;
		public int yHotspot;
		public IntPtr hbmMask;
		public IntPtr hbmColor;
	}

	[LibraryImport("gdi32.dll")]
	private static partial IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

	[LibraryImport("gdi32.dll")]
	private static partial IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr data);

	[LibraryImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DeleteObject(IntPtr handle);

	[LibraryImport("user32.dll")]
	private static partial IntPtr CreateIconIndirect(ref ICONINFO info);
}
