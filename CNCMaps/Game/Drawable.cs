using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CNCMaps.FileFormats;
using CNCMaps.Map;
using CNCMaps.Rendering;
using CNCMaps.VirtualFileSystem;

namespace CNCMaps.Game {
	public class Drawable {
		static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
		internal static readonly VoxelRenderer VoxelRenderer = new VoxelRenderer();

		public PaletteType PaletteType { get; set; }
		public LightingType LightingType { get; set; }
		public string CustomPaletteName { get; set; }
		public bool IsRemapable { get; set; }
		public bool InvisibleInGame { get; set; }

		internal IniFile.IniSection Rules { get; set; }
		internal IniFile.IniSection Art { get; set; }

		public Drawable(string name) {
			Name = name;
			Foundation = new Size(1, 1);
		}

		bool sorted;
		void Sort() {
			// orderby() is stable sort, .sort() isnt
			_fires = _fires.OrderBy(s => s.Props.SortIndex).ToList();
			_shps = _shps.OrderBy(s => s.Props.SortIndex).ToList();
			_damagedShps = _damagedShps.OrderBy(s => s.Props.SortIndex).ToList();
			//  _voxels.Sort();
			sorted = true;
		}

		Point _globalOffset = new Point(0, 0);

		public string Name { get; private set; }
		public Size Foundation { get; set; }
		public bool Overrides { get; set; }
		public bool IsWall { get; set; }
		public bool IsGate { get; set; }
		public bool IsVeins { get; set; }
		public int HeightOffset { get; set; }
		public bool DrawFlat { get; set; }

		// below are all the different kinds of drawables that a Drawable can consist of
		readonly List<DrawableFile<VxlFile>> _voxels = new List<DrawableFile<VxlFile>>();
		readonly List<HvaFile> _hvas = new List<HvaFile>();
		List<DrawableFile<ShpFile>> _shps = new List<DrawableFile<ShpFile>>();
		List<DrawableFile<ShpFile>> _fires = new List<DrawableFile<ShpFile>>();
		List<DrawableFile<ShpFile>> _damagedShps = new List<DrawableFile<ShpFile>>();
		private DrawableFile<ShpFile> _alphaImage;

		internal void SetAlphaImage(ShpFile alphaSHP) {
			_alphaImage = new DrawableFile<ShpFile>(alphaSHP, new DrawProperties {
				Offset = new Point(0, 15),
				FrameDecider = FrameDeciders.AlphaImageFrameDecider(alphaSHP),
			});
		}

		public virtual void Draw(GameObject obj, DrawingSurface ds) {
			logger.Trace("Drawing object {0} (type {1})", obj, obj.GetType());

			if (InvisibleInGame) return; // LOL. waste of effort much?

			if (!sorted) Sort();

			int direction = 0;
			if (obj is OwnableObject)
				direction = (obj as OwnableObject).Direction;

			if (obj is StructureObject && (obj as StructureObject).Health < 128) {
				foreach (var v in _damagedShps)
					DrawFile(obj, ds, v.File, v.Props);
				foreach (var v in _fires)
					DrawFile(obj, ds, v.File, v.Props);
			}
			else {
				foreach (var v in _shps)
					DrawFile(obj, ds, v.File, v.Props);

				if (_alphaImage != null)
					_alphaImage.File.DrawAlpha(obj, _alphaImage.Props, ds, _globalOffset);
			}

			for (int i = 0; i < _voxels.Count; i++) {
				// render voxel
				DrawingSurface vxl_ds = VoxelRenderer.Render(_voxels[i].File, _hvas[i], -(double)direction / 256.0 * 360 + 45, obj.Palette);
				if (vxl_ds != null)
					BlitVoxelToSurface(ds, vxl_ds, obj, _voxels[i].Props);
			}

		}

		private unsafe void BlitVoxelToSurface(DrawingSurface ds, DrawingSurface vxl_ds, GameObject obj, DrawProperties props) {
			Point d = new Point(obj.Tile.Dx * TileWidth / 2, (obj.Tile.Dy - obj.Tile.Z) * TileHeight / 2);
			d.Offset(_globalOffset);
			d.Offset(props.GetOffset(obj));
			d.Offset(-vxl_ds.bmd.Width / 2, -vxl_ds.bmd.Height / 2);

			// rows inverted!
			var w_low = (byte*)ds.bmd.Scan0;
			byte* w_high = w_low + ds.bmd.Stride * ds.bmd.Height;
			var zBuffer = ds.GetZBuffer();
			int rowsTouched = 0;

			for (int y = 0; y < vxl_ds.Height; y++) {
				short firstRowTouched = short.MaxValue;

				byte* src_row = (byte*)vxl_ds.bmd.Scan0 + vxl_ds.bmd.Stride * (vxl_ds.Height - y - 1);
				byte* dst_row = ((byte*)ds.bmd.Scan0 + (d.Y + y) * ds.bmd.Stride + d.X * 3);
				int zIdx = (d.Y + y) * ds.Width + d.X;
				if (dst_row < w_low || dst_row >= w_high) continue;

				for (int x = 0; x < vxl_ds.Width; x++) {
					// only non-transparent pixels
					if (*(src_row + x * 4 + 3) > 0) {
						*(dst_row + x * 3) = *(src_row + x * 4);
						*(dst_row + x * 3 + 1) = *(src_row + x * 4 + 1);
						*(dst_row + x * 3 + 2) = *(src_row + x * 4 + 2);

						if (y < firstRowTouched)
							firstRowTouched = (short)y;

						short zBufVal = (short)((obj.Tile.Rx + obj.Tile.Ry + obj.Tile.Z) * TileHeight / 2 + y - firstRowTouched);
						if (zBufVal >= zBuffer[zIdx])
							zBuffer[zIdx] = zBufVal;
					}
					zIdx++;
				}
			}
		}

		private void DrawFile(GameObject obj, DrawingSurface ds, ShpFile file, DrawProperties props) {
			if (file == null || obj == null || obj.Tile == null) return;

			file.Draw(obj, props, ds, _globalOffset);
			if (props.HasShadow) {
				file.DrawShadow(obj, props, ds, _globalOffset);
			}
		}
		internal void SetOffset(int xOffset, int yOffset) {
			_globalOffset.X = xOffset;
			_globalOffset.Y = yOffset;
		}

		internal void AddOffset(int extraXOffset, int extraYOffset) {
			_globalOffset.X += extraXOffset;
			_globalOffset.Y += extraYOffset;
		}

		internal void AddVoxel(VxlFile vxlFile, HvaFile hvaFile, DrawProperties props) {
			_voxels.Add(new DrawableFile<VxlFile>(vxlFile, props));
			_hvas.Add(hvaFile);
		}

		internal void AddShp(ShpFile shpFile, DrawProperties props) {
			var d = new DrawableFile<ShpFile>(shpFile, props);
			_shps.Add(d);
		}

		internal void AddDamagedShp(ShpFile shpFile, DrawProperties props) {
			_damagedShps.Add(new DrawableFile<ShpFile>(shpFile, props));
		}

		internal void AddFire(ShpFile shpFile, DrawProperties props) {
			_fires.Add(new DrawableFile<ShpFile>(shpFile, props));
		}

		public static ushort TileWidth { get; set; }
		public static ushort TileHeight { get; set; }
		public bool IsValid { get; set; }

		public override string ToString() {
			return Name;
		}

	}

	class DrawableFile<T> : System.IComparable where T : VirtualFile {
		public DrawProperties Props;
		public T File;

		public DrawableFile(T file) {
			this.File = file;
			Props = new DrawProperties();
		}

		public DrawableFile(T file, DrawProperties drawProperties) {
			this.File = file;
			Props = drawProperties;
		}

		public int CompareTo(object obj) {
			return Props.SortIndex.CompareTo((obj as DrawableFile<T>).Props.SortIndex);
		}
	}
}