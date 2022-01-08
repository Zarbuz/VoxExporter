using FileToVoxCore.Schematics.Tools;
using FileToVoxCore.Utils;
using FileToVoxCore.Vox;
using FileToVoxCore.Vox.Chunks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileToVoxCore.Drawing;

namespace VoxExporter
{
	public class VoxWriterCustom : VoxParser
	{
		private VoxModel mModel;
		private int mTotalBlockCount;
		public bool WriteModel(string absolutePath, VoxModel model)
		{
			List<VoxModel> list = GetVoxelModelsFromRegions(model);
			return WriteVoxForEachTransforms(absolutePath, list);
		}

		private List<VoxModel> GetVoxelModelsFromRegions(VoxModel voxModel)
		{
			List<VoxModel> result = new List<VoxModel>();

			foreach (TransformNodeChunk transformNodeChunk in voxModel.TransformNodeChunks)
			{
				ShapeNodeChunk shapeNodeChunk = voxModel.ShapeNodeChunks.Find(shape => shape.Id == transformNodeChunk.ChildId);
				if (shapeNodeChunk == null)
				{
					continue;
				}

				transformNodeChunk.Id = 2;
				VoxModel model = new VoxModel
				{
					Palette = voxModel.Palette,
					MaterialChunks = voxModel.MaterialChunks,
					LayerChunks = voxModel.LayerChunks,
					TransformNodeChunks = new List<TransformNodeChunk> {transformNodeChunk},
					ShapeNodeChunks = new List<ShapeNodeChunk>
					{
						shapeNodeChunk
					},
					VoxelFrames = new List<VoxelData>
					{
						voxModel.VoxelFrames[shapeNodeChunk.Models[0].ModelId]
					},
					GroupNodeChunks = new List<GroupNodeChunk>(),
					PaletteColorIndex = voxModel.PaletteColorIndex
				};

				model.GroupNodeChunks.Add(new GroupNodeChunk()
				{
					Id = 1,
					ChildIds = new[] { 2 },
					Attributes = new KeyValue[] { }
				});
				result.Add(model);
			}
			return result;
		}

		private bool WriteVoxForEachTransforms(string absolutePath, List<VoxModel> list)
		{
			for (int i = 0; i < list.Count; i++)
			{
				string path = absolutePath + "_" + i + ".vox";
				mModel = list[i];
				using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
				{
					writer.Write(Encoding.UTF8.GetBytes(HEADER));
					writer.Write(VERSION);
					writer.Write(Encoding.UTF8.GetBytes(MAIN));
					writer.Write(0);
					int childrenSize = CountChildrenSize();
					writer.Write(childrenSize);
					int byteWritten = WriteChunks(writer);

					Console.WriteLine("[LOG] Bytes to write for childs chunks: " + childrenSize);
					Console.WriteLine("[LOG] Bytes written: " + byteWritten);
					if (byteWritten != childrenSize)
					{
						Console.WriteLine("[LOG] Children size and bytes written isn't the same! Vox is corrupted!");
						Console.ReadKey();
						return false;
					}
				}
			}


			return true;
		}

		private int CountChildrenSize()
		{
			mTotalBlockCount = CountTotalBlocks();
			Console.WriteLine("[INFO] Total blocks: " + mTotalBlockCount);

			int chunkSIZE = 24; //24 = 12 bytes for header and 12 bytes of content
			int chunkXYZI = (16) + mTotalBlockCount * 4; //16 = 12 bytes for header and 4 for the voxel count + (number of voxels) * 4
			int chunknTRN = CountTransformChunkSize();
			int chunknSHP = CountShapeChunkSize();
			int chunkRGBA = 1024 + 12;
			int chunkMATL = CountMaterialChunksize();
			int chunkMnTRN = 40;
			int chunkMnGRP = CountMainGroupChunkSize();
			int chunkIMAP = CountIMAPChunkSize();

			Console.WriteLine("[LOG] Chunk RGBA: " + chunkRGBA);
			Console.WriteLine("[LOG] Chunk MATL: " + chunkMATL);
			Console.WriteLine("[LOG] Chunk SIZE: " + chunkSIZE);
			Console.WriteLine("[LOG] Chunk XYZI: " + chunkXYZI);
			Console.WriteLine("[LOG] Chunk nTRN: " + chunknTRN);
			Console.WriteLine("[LOG] Chunk nSHP: " + chunknSHP);
			Console.WriteLine("[LOG] Chunk MnTRN: " + chunkMnTRN);
			Console.WriteLine("[LOG] Chunk MnGRP: " + chunkMnGRP);
			Console.WriteLine("[LOG] Chunk IMAP: " + chunkIMAP);

			int childrenChunkSize = chunkSIZE; //SIZE CHUNK
			childrenChunkSize += chunkXYZI; //XYZI CHUNK
			childrenChunkSize += chunknTRN; //nTRN CHUNK
			childrenChunkSize += chunknSHP;
			childrenChunkSize += chunkRGBA;
			childrenChunkSize += chunkMATL;
			childrenChunkSize += chunkMnTRN;
			childrenChunkSize += chunkMnGRP;
			childrenChunkSize += chunkIMAP;

			return childrenChunkSize;
		}

		private int CountTotalBlocks()
		{
			return mModel.VoxelFrames.Sum(data => data.Colors.Count);
		}

		/// <summary>
		/// Main loop for write all chunks
		/// </summary>
		/// <param name="writer"></param>
		private int WriteChunks(BinaryWriter writer)
		{
			int byteWritten = 0;

			int SIZE = 0;
			int XYZI = 0;

			Console.WriteLine("[LOG] Step [1/2]: Started to write SIZE and XYZI...");
			using (var progressbar = new ProgressBar())
			{
				int totalModels = mModel.VoxelFrames.Count;
				int indexProgression = 0;
				for (int j = 0; j < mModel.VoxelFrames.Count; j++)
				{
					SIZE += WriteSizeChunk(writer, mModel.VoxelFrames[j].GetVolumeSize());
					XYZI += WriteXyziChunk(writer, mModel, j);
					float progress = indexProgression / (float)totalModels;
					progressbar.Report(progress);
					indexProgression++;
				}
			}
			Console.WriteLine("[LOG] Done.");

			int nGRP = 0;
			int nTRN = 0;
			int nSHP = 0;

			int mnTRN = WriteMainTransformChunk(writer);
			int indexChunk = 2;
			List<int> mainGroupIds = new List<int>();
			mainGroupIds.Add(2);

			int mnGRP = WriteMainGroupChunk(writer, mModel.TransformNodeChunks.Select(t => t.Id).ToList());

			Console.WriteLine("[LOG] Step [2/2]: Started to write nTRN, nGRP and nSHP chunks...");
			using (var progressbar = new ProgressBar())
			{
				Dictionary<int, int> modelIds = new Dictionary<int, int>();
				Dictionary<int, int> shapeIds = new Dictionary<int, int>();
				int indexModel = 0;
				mainGroupIds.Clear();
				mainGroupIds.Add(2);
				for (int i = 0; i < mModel.TransformNodeChunks.Count; i++)
				{
					int childId = mModel.TransformNodeChunks[i].ChildId;

					int transformIndexUnique = indexChunk++;

					ShapeNodeChunk shapeNode = mModel.ShapeNodeChunks.FirstOrDefault(t => t.Id == childId);
					if (shapeNode != null)
					{
						int modelId = shapeNode.Models[0].ModelId;
						int modelIndexUnique = modelId;

						if (!modelIds.ContainsKey(modelIndexUnique))
						{
							modelIds.Add(modelIndexUnique, indexModel);
							indexModel++;
						}

						int shapeIndexUnique = shapeNode.Id + 2; //Hack
						nTRN += WriteTransformChunk(writer, mModel.TransformNodeChunks[i], transformIndexUnique, shapeIds.ContainsKey(shapeIndexUnique) ? shapeIds[shapeIndexUnique] : indexChunk);

						if (!shapeIds.ContainsKey(shapeIndexUnique))
						{
							shapeIds.Add(shapeIndexUnique, indexChunk);
							nSHP += WriteShapeChunk(writer, indexChunk, modelIds[modelIndexUnique]);
							indexChunk++;
						}
					}

					progressbar.Report(i / (float)mModel.TransformNodeChunks.Count);
				}

				int max = mModel.TransformNodeChunks.Max(t => t.Id);
				mainGroupIds.Add(max + (max % 2 == 0 ? 2 : 1) + mainGroupIds.Last());
			}

			int RGBA = WritePaletteChunk(writer);
			int MATL = mModel.MaterialChunks.Sum(materialChunk => WriteMaterialChunk(writer, materialChunk, materialChunk.Id));
			int IMAP = WriteIMAPChunk(writer);

			Console.WriteLine("[LOG] Written RGBA: " + RGBA);
			Console.WriteLine("[LOG] Written MATL: " + MATL);
			Console.WriteLine("[LOG] Written SIZE: " + SIZE);
			Console.WriteLine("[LOG] Written XYZI: " + XYZI);
			Console.WriteLine("[LOG] Written nGRP: " + nGRP);
			Console.WriteLine("[LOG] Written nTRN: " + nTRN);
			Console.WriteLine("[LOG] Written nSHP: " + nSHP);
			Console.WriteLine("[LOG] Written mnTRN: " + mnTRN);
			Console.WriteLine("[LOG] Written mnGRP: " + mnGRP);
			Console.WriteLine("[LOG] Written IMAP: " + IMAP);

			byteWritten = RGBA + MATL + SIZE + XYZI + nGRP + nTRN + nSHP + mnTRN + mnGRP + IMAP;
			return byteWritten;
		}

		/// <summary>
		/// Write the main nTRN chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <returns></returns>
		private int WriteMainTransformChunk(BinaryWriter writer)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(nTRN));
			writer.Write(28); //Main nTRN has always a size of 28 bytes
			writer.Write(0); //Child nTRN chunk size
			writer.Write(0); //ID of nTRN
			writer.Write(0); //ReadDICT size for attributes
			writer.Write(1); //Child ID
			writer.Write(-1); //Reserved ID
			writer.Write(0); //Layer ID
			writer.Write(1); //Read Array Size
			writer.Write(0); //ReadDICT size

			byteWritten += Encoding.UTF8.GetByteCount(nTRN) + 36;
			return byteWritten;
		}

		private int WriteMainGroupChunk(BinaryWriter writer, List<int> ids)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(nGRP));
			writer.Write(12 + (4 * (ids.Count))); //nGRP chunk size
			writer.Write(0); //Child nGRP chunk size
			writer.Write(1); //ID of nGRP
			writer.Write(0); //Read DICT size for attributes (none)
			writer.Write(ids.Count);
			byteWritten += Encoding.UTF8.GetByteCount(nGRP) + 20;

			for (int i = 0; i < ids.Count; i++)
			{
				writer.Write(ids[i]); //Write the ID of child group
				byteWritten += 4;
			}


			return byteWritten;
		}

		/// <summary>
		/// Write nTRN chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private int WriteTransformChunk(BinaryWriter writer, TransformNodeChunk transformNode, int id, int childId)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(nTRN));
			byteWritten += Encoding.UTF8.GetByteCount(nTRN);

			Vector3 worldPosition = transformNode.TranslationAt();
			string pos = worldPosition.X + " " + worldPosition.Y + " " + worldPosition.Z;

			writer.Write(48 + Encoding.UTF8.GetByteCount(pos)
							+ Encoding.UTF8.GetByteCount(Convert.ToString((byte)transformNode.RotationAt()))); //nTRN chunk size
			writer.Write(0); //nTRN child chunk size
			writer.Write(id); //ID
			writer.Write(0); //ReadDICT size for attributes (none)
			writer.Write(childId);//Child ID
			writer.Write(-1); //Reserved ID
			writer.Write(transformNode.LayerId); //Layer ID
			writer.Write(1); //Read Array Size
			writer.Write(2); //Read DICT Size (previously 1)

			writer.Write(2); //Read STRING size
			byteWritten += 40;

			writer.Write(Encoding.UTF8.GetBytes("_r"));
			writer.Write(Encoding.UTF8.GetByteCount(Convert.ToString((byte)transformNode.RotationAt())));
			writer.Write(Encoding.UTF8.GetBytes(Convert.ToString((byte)transformNode.RotationAt())));

			byteWritten += Encoding.UTF8.GetByteCount("_r");
			byteWritten += 4;
			byteWritten += Encoding.UTF8.GetByteCount(Convert.ToString((byte)transformNode.RotationAt()));


			writer.Write(2); //Read STRING Size
			writer.Write(Encoding.UTF8.GetBytes("_t"));
			writer.Write(Encoding.UTF8.GetByteCount(pos));
			writer.Write(Encoding.UTF8.GetBytes(pos));

			byteWritten += 4 + Encoding.UTF8.GetByteCount("_t") + 4 + Encoding.UTF8.GetByteCount(pos);
			return byteWritten;
		}


		/// <summary>
		/// Write nSHP chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private int WriteShapeChunk(BinaryWriter writer, int id, int indexModel)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(nSHP));
			writer.Write(20); //nSHP chunk size
			writer.Write(0); //nSHP child chunk size
			writer.Write(id); //ID
			writer.Write(0);
			writer.Write(1);
			writer.Write(indexModel);
			writer.Write(0);

			byteWritten += Encoding.UTF8.GetByteCount(nSHP) + 28;
			return byteWritten;
		}

		/// <summary>
		/// Write nGRP chunk
		/// </summary>
		/// <param name="writer"></param>
		private int WriteGroupChunk(BinaryWriter writer, int id, List<int> ids)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(nGRP));
			writer.Write(12 + (4 * (ids.Count))); //nGRP chunk size
			writer.Write(0); //Child nGRP chunk size
			writer.Write(id); //ID of nGRP
			writer.Write(0); //Read DICT size for attributes (none)
			writer.Write(ids.Count);
			byteWritten += Encoding.UTF8.GetByteCount(nGRP) + 20;

			for (int i = 0; i < ids.Count; i++)
			{
				writer.Write(ids[i]); //Write the ID of child group
				byteWritten += 4;
			}


			return byteWritten;
		}

		/// <summary>
		/// Write SIZE chunk
		/// </summary>
		/// <param name="writer"></param>
		private int WriteSizeChunk(BinaryWriter writer, Vector3 volumeSize)
		{
			int byteWritten = 0;

			writer.Write(Encoding.UTF8.GetBytes(SIZE));
			writer.Write(12); //Chunk Size (constant)
			writer.Write(0); //Child Chunk Size (constant)

			writer.Write((int)volumeSize.X); //Width
			writer.Write((int)volumeSize.Y); //Height
			writer.Write((int)volumeSize.Z); //Depth

			byteWritten += Encoding.UTF8.GetByteCount(SIZE) + 20;
			return byteWritten;
		}

		/// <summary>
		/// Write XYZI chunk
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="index"></param>
		private int WriteXyziChunk(BinaryWriter writer, VoxModel model, int index)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(XYZI));
			//int testA = (model.VoxelFrames[index].Colors.Count(t => t != 0));
			//int testB = model.VoxelFrames[index].Colors.Length;
			writer.Write((model.VoxelFrames[index].Colors.Count * 4) + 4); //XYZI chunk size
			writer.Write(0); //Child chunk size (constant)
			writer.Write(model.VoxelFrames[index].Colors.Count); //Blocks count

			byteWritten += Encoding.UTF8.GetByteCount(XYZI) + 12;
			int count = 0;
			foreach (KeyValuePair<int, byte> entry in model.VoxelFrames[index].Colors)
			{
				int paletteIndex = entry.Value;
				int finalPaletteIndex = mModel.PaletteColorIndex?.ToList().IndexOf(paletteIndex) + 1 ?? paletteIndex;
				Color color = finalPaletteIndex >= model.Palette.Length ? Color.Empty : model.Palette[finalPaletteIndex];
				model.VoxelFrames[index].Get3DPos(entry.Key, out int x, out int y, out int z);
				if (color != Color.Empty)
				{
					writer.Write((byte)(x % model.VoxelFrames[index].VoxelsWide));
					writer.Write((byte)(y % model.VoxelFrames[index].VoxelsTall));
					writer.Write((byte)(z % model.VoxelFrames[index].VoxelsDeep));

					writer.Write((finalPaletteIndex != 0) ? (byte)finalPaletteIndex : (byte)1);
					count++;

					byteWritten += 4;
				}
			}

			return byteWritten;
		}

		/// <summary>
		/// Write RGBA chunk
		/// </summary>
		/// <param name="writer"></param>
		private int WritePaletteChunk(BinaryWriter writer)
		{
			int byteCount = 0;
			writer.Write(Encoding.UTF8.GetBytes(RGBA));
			writer.Write(1024);
			writer.Write(0);

			byteCount += Encoding.UTF8.GetByteCount(RGBA) + 8;
			for (int i = 0; i < mModel.Palette.Length; i++)
			{
				Color color;
				if (mModel.PaletteColorIndex != null)
				{
					color = i == 255 ? Color.Empty : mModel.Palette[mModel.PaletteColorIndex[i]];
				}
				else
				{
					color = i == 255 ? Color.Empty : mModel.Palette[i + 1];
				}
				//Color color = mModel.Palette[i];

				writer.Write(color.R);
				writer.Write(color.G);
				writer.Write(color.B);
				writer.Write(color.A);
				byteCount += 4;
			}

			for (int i = (256 - mModel.Palette.Length); i >= 1; i--)
			{
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((byte)0);
				byteCount += 4;
			}

			return byteCount;
		}


		/// <summary>
		/// Write MATL chunk
		/// </summary>
		/// <param name="writer"></param>
		private int WriteMaterialChunk(BinaryWriter writer, MaterialChunk materialChunk, int index)
		{
			int byteWritten = 0;
			writer.Write(Encoding.UTF8.GetBytes(MATL));
			writer.Write(GetMaterialPropertiesSize(materialChunk.Properties) + 8);
			writer.Write(0); //Child Chunk Size (constant)

			writer.Write(index); //Id
			writer.Write(materialChunk.Properties.Length); //ReadDICT size

			byteWritten += Encoding.UTF8.GetByteCount(MATL) + 16;

			foreach (KeyValue keyValue in materialChunk.Properties)
			{
				writer.Write(Encoding.UTF8.GetByteCount(keyValue.Key));
				writer.Write(Encoding.UTF8.GetBytes(keyValue.Key));
				writer.Write(Encoding.UTF8.GetByteCount(keyValue.Value));
				writer.Write(Encoding.UTF8.GetBytes(keyValue.Value));

				byteWritten += 8 + Encoding.UTF8.GetByteCount(keyValue.Key) + Encoding.UTF8.GetByteCount(keyValue.Value);
			}

			return byteWritten;
		}

		private int WriteIMAPChunk(BinaryWriter writer)
		{
			int byteWritten = 0;

			if (mModel.PaletteColorIndex != null)
			{
				writer.Write(Encoding.UTF8.GetBytes(IMAP));
				byteWritten += Encoding.UTF8.GetByteCount(MATL);

				foreach (int paletteIndex in mModel.PaletteColorIndex)
				{
					writer.Write((byte)paletteIndex);
					byteWritten++;
				}
			}

			return byteWritten;
		}

		private int GetMaterialPropertiesSize(KeyValue[] properties)
		{
			return properties.Sum(keyValue => 8 + Encoding.UTF8.GetByteCount(keyValue.Key) + Encoding.UTF8.GetByteCount(keyValue.Value));
		}


		/// <summary>
		/// Count the size of all materials chunks
		/// </summary>
		/// <returns></returns>
		private int CountMaterialChunksize()
		{
			int size = 0;
			for (int i = 0; i < mModel.Palette.Length; i++)
			{
				size += Encoding.UTF8.GetByteCount(MATL) + 16;
				size += mModel.MaterialChunks[i].Properties.Sum(keyValue => 8 + Encoding.UTF8.GetByteCount(keyValue.Key) + Encoding.UTF8.GetByteCount(keyValue.Value));
			}

			return size;
		}


		/// <summary>
		/// Count the size of all nTRN chunks
		/// </summary>
		/// <returns></returns>
		private int CountTransformChunkSize()
		{
			int size = 0;
			for (int i = 0; i < mModel.TransformNodeChunks.Count; i++)
			{
				Vector3 worldPosition = mModel.TransformNodeChunks[i].TranslationAt();
				Rotation rotation = mModel.TransformNodeChunks[i].RotationAt();

				string pos = worldPosition.X + " " + worldPosition.Y + " " + worldPosition.Z;

				size += Encoding.UTF8.GetByteCount(nTRN);
				size += 40;


				size += Encoding.UTF8.GetByteCount("_r");
				size += 4;
				size += Encoding.UTF8.GetByteCount(Convert.ToString((byte)rotation));
				size += 4 + Encoding.UTF8.GetByteCount("_t") + 4 + Encoding.UTF8.GetByteCount(pos);
			}

			return size;
		}

		/// <summary>
		/// Count the size of all nSHP chunks
		/// </summary>
		/// <returns></returns>
		private int CountShapeChunkSize()
		{
			int size = 0;
			List<int> shapeIds = new List<int>();
			for (int j = 0; j < mModel.ShapeNodeChunks.Count; j++)
			{
				ShapeNodeChunk shapeNode = mModel.ShapeNodeChunks[j];
				int id = shapeNode.Id + 2;
				if (!shapeIds.Contains(id))
				{
					shapeIds.Add(id);
					size += Encoding.UTF8.GetByteCount(nSHP) + 28;
				}
			}
			return size;
		}


		/// <summary>
		/// Count the size of all nGRP chunks
		/// </summary>
		/// <returns></returns>
		private int CountGroupChunkSize()
		{
			int byteWritten = 0;
			for (int i = 0; i < mModel.GroupNodeChunks.Count; i++)
			{
				byteWritten += Encoding.UTF8.GetByteCount(nGRP) + 20;
				byteWritten += mModel.GroupNodeChunks[i].ChildIds.ToList().Sum(id => 4);
			}

			return byteWritten;
		}

		/// <summary>
		/// Count the size of the main nGRP chunk
		/// </summary>
		/// <returns></returns>
		private int CountMainGroupChunkSize()
		{
			int byteWritten = 0;
			byteWritten += Encoding.UTF8.GetByteCount(nGRP) + 20;
			byteWritten += 4;

			return byteWritten;
		}

		private int CountIMAPChunkSize()
		{
			return mModel.PaletteColorIndex != null ? Encoding.UTF8.GetByteCount(IMAP) + 256 : 0;
		}
	}
}
