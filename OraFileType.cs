using PaintDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;

namespace OpenRasterFileType {
    /// <summary>
    /// Implements Paint.NET FileType support for the OpenRaster (.ora) image exchange format.
    /// OpenRaster is an open standard designed for layered raster graphics interchange between applications
    /// such as Paint.NET, Krita, MyPaint, and GIMP.
    /// </summary>
    public class OraFileType : FileType {
        /// <summary>
        /// Maximum dimension (width or height) in pixels for the generated OpenRaster thumbnail preview.
        /// </summary>
        private const int thumbMaxSize = 256;

        /// <summary>
        /// Base64 encoded .zip archive containing an uncompressed "mimetype" file with content "image/openraster".
        /// In OpenRaster specifications, the "mimetype" file must appear uncompressed at the beginning of the archive.
        /// </summary>
        private readonly string mimeTypeZip = "UEsDBBQAAAAAAAAAIQDHmvCMEAAAABAAAAAIAAAAbWltZXR5cGVpbWFnZS9vcGVucmFzdGVyUEsBAhQDFAAAAAAAAAAhAMea8IwQAAAAEAAAAAgAAAAAAAAAAAAAAKSBAAAAAG1pbWV0eXBlUEsFBgAAAAABAAEANgAAADYAAAAAAA==";

        /// <summary>
        /// Mapping from Paint.NET <see cref="LayerBlendMode"/> enum values to OpenRaster SVG composite operation identifiers.
        /// </summary>
        private static readonly Dictionary<LayerBlendMode, string> blendDict = new() {
            { LayerBlendMode.Additive, "svg:plus" },
            { LayerBlendMode.ColorBurn, "svg:color-burn" },
            { LayerBlendMode.ColorDodge, "svg:color-dodge" },
            { LayerBlendMode.Darken, "svg:darken" },
            { LayerBlendMode.Difference, "svg:difference" },
            { LayerBlendMode.Glow, "pdn:glow" },
            { LayerBlendMode.Lighten, "svg:lighten" },
            { LayerBlendMode.Multiply, "svg:multiply" },
            { LayerBlendMode.Negation, "pdn:negation" },
            { LayerBlendMode.Normal, "svg:src-over" },
            { LayerBlendMode.Overlay, "svg:overlay" },
            { LayerBlendMode.Reflect, "pdn:reflect" },
            { LayerBlendMode.Screen, "svg:screen" },
            { LayerBlendMode.Xor, "svg:xor" },
        };

        /// <summary>
        /// Reverse lookup mapping from SVG / Paint.NET composite operation strings back to <see cref="LayerBlendMode"/>.
        /// </summary>
        private static readonly Dictionary<string, LayerBlendMode> SVGDict = blendDict.ToDictionary(x => x.Value, x => x.Key);

        /// <summary>
        /// Known MyPaint stroke map metadata attribute names supported during load/save operations.
        /// </summary>
        private readonly string[] strokeMapVersions;

        /// <summary>
        /// Initializes a new instance of the <see cref="OraFileType"/> class, configuring file extensions and layer support.
        /// </summary>
        public OraFileType() : base("OpenRaster",
            new FileTypeOptions() {
                SupportsLayers = true,
                LoadExtensions = [".ora"],
                SaveExtensions = [".ora"]
            }
        ){
            strokeMapVersions = ["mypaint_strokemap", "mypaint_strokemap_v2"];
        }

        /// <summary>
        /// Loads an OpenRaster (.ora) file from a stream and constructs a multi-layered Paint.NET <see cref="Document"/>.
        /// </summary>
        /// <param name="input">The stream containing the OpenRaster archive.</param>
        /// <returns>A populated Paint.NET <see cref="Document"/> instance.</returns>
        /// <exception cref="FormatException">Thrown if the mimetype, stack.xml, or layers are missing or invalid.</exception>
        protected override Document OnLoad(Stream input) {
            using ZipArchive file = new(input, ZipArchiveMode.Read);
            try {
                ZipArchiveEntry mimeEntry = file.GetEntry("mimetype");
                using StreamReader reader = new(mimeEntry.Open());
                string mimeType = reader.ReadToEnd();
                if (!mimeType.Equals("image/openraster", StringComparison.Ordinal)) {
                    throw new FormatException("Incorrect mimetype: " + mimeType);
                }
            }
            catch (NullReferenceException) {
                throw new FormatException("No mimetype found in OpenRaster file");
            }

            XmlDocument stackXml = new();
            try {
                stackXml.Load(file.GetEntry("stack.xml").Open());
            }
            catch (NullReferenceException) {
                throw new FormatException("No 'stack.xml' found in OpenRaster file");
            }

            XmlElement imageElement = stackXml.DocumentElement;
            int width = int.Parse(GetAttribute(imageElement, "w", "0"), CultureInfo.InvariantCulture);
            int height = int.Parse(GetAttribute(imageElement, "h", "0"), CultureInfo.InvariantCulture);

            Document doc = new(width, height) {
                DpuUnit = MeasurementUnit.Inch,
                DpuX = double.Parse(GetAttribute(imageElement, "xres", "72"), CultureInfo.InvariantCulture),
                DpuY = double.Parse(GetAttribute(imageElement, "yres", "72"), CultureInfo.InvariantCulture)
            };

            XmlElement stackElement = (XmlElement)stackXml.GetElementsByTagName("stack")[0];
            XmlNodeList layerElements = stackElement.GetElementsByTagName("layer");

            if (layerElements.Count == 0) {
                throw new FormatException("No layers found in OpenRaster file");
            }

            int layerCount = layerElements.Count - 1;

            for (int i = layerCount; i >= 0; i--) { // The last layer in the list is the background so load in reverse
                XmlElement layerElement = (XmlElement)layerElements[i];
                int x = int.Parse(GetAttribute(layerElement, "x", "0"), CultureInfo.InvariantCulture); // the x offset within the layer
                int y = int.Parse(GetAttribute(layerElement, "y", "0"), CultureInfo.InvariantCulture); // the y offset within the layer

                int layerNum = layerCount - i;

                ZipArchiveEntry layerEntry = file.GetEntry(layerElement.GetAttribute("src"));

                using Stream s = layerEntry.Open();
                using Bitmap BMP = GetBitmapFromOraLayer(x, y, s, width, height);

                BitmapLayer myLayer = i == layerCount ? Layer.CreateBackgroundLayer(width, height) : new(width, height);

                myLayer.Surface.CopyFromGdipBitmap(BMP, false);

                myLayer.Name = GetAttribute(layerElement, "name", $"Layer {layerNum}");
                myLayer.Opacity = (byte)(255.0 * double.Parse(GetAttribute(layerElement, "opacity", "1"), CultureInfo.InvariantCulture));
                myLayer.Visible = GetAttribute(layerElement, "visibility", "visible") == "visible"; // newer ora files have this

                string compOp = GetAttribute(layerElement, "composite-op", "svg:src-over");

                compOp = compOp.Contains("pdn-") ? compOp.Replace("pdn-", "pdn:") : compOp;

                if (SVGDict.TryGetValue(compOp, out LayerBlendMode value)) {
                    myLayer.BlendMode = value;
                }
                else {
                    string pdnCompOp = "pdn:" + compOp.Split(':')[1];
                    myLayer.BlendMode = SVGDict.TryGetValue(pdnCompOp, out LayerBlendMode pdnValue) ? pdnValue : LayerBlendMode.Normal;
                }

                string backTile = GetAttribute(layerElement, "background_tile", string.Empty);

                if (!string.IsNullOrEmpty(backTile)) {
                    // convert the tile image to a Base64String and then save it in the layer's MetaData.
                    myLayer.Metadata.SetUserValue("OraBackgroundTile", ToBase64(file.GetEntry(backTile)));
                }

                foreach (string version in strokeMapVersions) {
                    string strokeMap = GetAttribute(layerElement, version, string.Empty);

                    if (!string.IsNullOrEmpty(strokeMap)) {
                        // convert the stroke map to a Base64String and then save it in the layer's MetaData.
                        myLayer.Metadata.SetUserValue("OraMyPaintStrokeMapData", ToBase64(file.GetEntry(strokeMap)));
                        // Save the version of the stroke map in the MetaData
                        myLayer.Metadata.SetUserValue("OraMyPaintStrokeMapVersion", version);
                    }
                }
                doc.Layers.Insert(layerNum, myLayer);
            }
            return doc;
        }

        /// <summary>
        /// Saves a Paint.NET <see cref="Document"/> to an OpenRaster (.ora) compliant ZIP archive.
        /// Generates the layer PNG images, stack.xml, mergedimage.png composite, and preview thumbnail.
        /// </summary>
        /// <param name="input">The document to save.</param>
        /// <param name="output">The output stream to write the OpenRaster archive to.</param>
        /// <param name="token">Save configuration token (unused for default settings).</param>
        /// <param name="scratchSurface">Scratch surface provided by Paint.NET for temporary drawing.</param>
        /// <param name="callback">Progress reporting callback delegate.</param>
        /// <exception cref="ArgumentNullException">Thrown if input or output is null.</exception>
        /// <exception cref="InvalidEnumArgumentException">Thrown if measurement unit is unsupported.</exception>
        protected override void OnSave(Document input, Stream output, SaveConfigToken token, Surface scratchSurface, ProgressEventHandler callback) {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);

            byte[] zipBytes = Convert.FromBase64String(mimeTypeZip);

            output.Write(zipBytes, 0, zipBytes.Length);

            using ZipArchive archive = new(output, ZipArchiveMode.Update, true);

            LayerInfo[] layerInfo = new LayerInfo[input.Layers.Count];

            for (int i = 0; i < input.Layers.Count; i++) {
                BitmapLayer layer = (BitmapLayer)input.Layers[i];
                Rectangle bounds = layer.Surface.Bounds;
                ColorBgra pixel;

                int left = layer.Width;
                int top = layer.Height;
                int right = 0;
                int bottom = 0;
                for (int y = 0; y < layer.Height; y++) {
                    for (int x = 0; x < layer.Width; x++) {
                        pixel = layer.Surface[x, y];
                        if (pixel.A > 0) {
                            left = x < left ? x : left;
                            right = x > right ? x : right;
                            top = y < top ? y : top;
                            bottom = y > bottom ? y : bottom;
                        }
                    }
                }

                if (left < layer.Width && top < layer.Height) { // is the layer not empty
                    bounds = new Rectangle(left, top, right - left + 1, bottom - top + 1); // clip it to the visible rectangle
                    layerInfo[i] = new LayerInfo(left, top);
                }
                else {
                    layerInfo[i] = new LayerInfo(0, 0);
                }

                string tileData = layer.Metadata.GetUserValue("OraBackgroundTile");

                if (!string.IsNullOrEmpty(tileData)) { // save the background_tile png if it exists
                    ZipArchiveEntry bgTile = archive.CreateEntry("data/background_tile.png");

                    using Stream bg = bgTile.Open();
                    byte[] tileBytes = Convert.FromBase64String(tileData);
                    bg.Write(tileBytes, 0, tileBytes.Length);
                }

                string strokeData = layer.Metadata.GetUserValue("OraMyPaintStrokeMapData");

                if (!string.IsNullOrEmpty(strokeData)) { // save MyPaint's stroke data if it exists
                    ZipArchiveEntry strokeMap = archive.CreateEntry("data/layer" + i.ToString(CultureInfo.InvariantCulture) + "_strokemap.dat");

                    using Stream stroke = strokeMap.Open();
                    byte[] strokeBytes = Convert.FromBase64String(strokeData);
                    stroke.Write(strokeBytes, 0, strokeBytes.Length);
                }

                using MemoryStream layerStream = new();
                layer.Surface.CreateAliasedBitmap(bounds, true).Save(layerStream, ImageFormat.Png);
                byte[] layerBuf = layerStream.ToArray();

                ZipArchiveEntry layerPNG = archive.CreateEntry("data/layer" + i.ToString(CultureInfo.InvariantCulture) + ".png");

                using Stream pngStream = layerPNG.Open();
                pngStream.Write(layerBuf, 0, layerBuf.Length);
            }

            ZipArchiveEntry stackXML = archive.CreateEntry("stack.xml");

            using (Stream sXML = stackXML.Open()) {
                double dpiX;
                double dpiY;

                switch (input.DpuUnit) {
                    case MeasurementUnit.Centimeter:
                        dpiX = Document.DotsPerCmToDotsPerInch(input.DpuX);
                        dpiY = Document.DotsPerCmToDotsPerInch(input.DpuY);
                        break;

                    case MeasurementUnit.Inch:
                        dpiX = input.DpuX;
                        dpiY = input.DpuY;
                        break;

                    case MeasurementUnit.Pixel:
                        dpiX = Document.GetDefaultDpu(MeasurementUnit.Inch);
                        dpiY = Document.GetDefaultDpu(MeasurementUnit.Inch);
                        break;

                    default:
                        throw new InvalidEnumArgumentException("Invalid measurement unit.");
                }

                byte[] stackBytes = GetLayerXmlData(input.Layers, layerInfo, dpiX, dpiY);
                sXML.Write(stackBytes, 0, stackBytes.Length);
            }

            using Surface flat = new(input.Width, input.Height);

            input.Flatten(flat);

            using MemoryStream aliasStream = new();
            flat.CreateAliasedBitmap().Save(aliasStream, ImageFormat.Png);
            byte[] aliasBytes = aliasStream.ToArray();

            ZipArchiveEntry mergeEntry = archive.CreateEntry("mergedimage.png");

            using Stream merge = mergeEntry.Open();
            merge.Write(aliasBytes, 0, aliasBytes.Length);

            Size thumbSize = GetThumbDimensions(input.Width, input.Height);

            using Surface scale = new(thumbSize);
            scale.FitSurface(ResamplingAlgorithm.SuperSampling, flat);

            using MemoryStream scaleStream = new();
            scale.CreateAliasedBitmap().Save(scaleStream, ImageFormat.Png);

            byte[] scaleBytes = scaleStream.ToArray();

            ZipArchiveEntry thumbnailEntry = archive.CreateEntry("Thumbnails/thumbnail.png");
            using Stream thumbStream = thumbnailEntry.Open();
            thumbStream.Write(scaleBytes, 0, scaleBytes.Length);
        }

        /// <summary>
        /// Stores upper-left pixel coordinates (x, y) representing layer offsets relative to the main image canvas.
        /// </summary>
        private struct LayerInfo(int x, int y) {
            public int x = x;
            public int y = y;
        }

        /// <summary>
        /// Reads an individual layer's PNG image stream and paints it onto a canvas of dimensions matching the target document.
        /// </summary>
        /// <param name="xofs">The horizontal offset in pixels of the layer.</param>
        /// <param name="yofs">The vertical offset in pixels of the layer.</param>
        /// <param name="inStream">Stream providing the layer PNG data.</param>
        /// <param name="baseWidth">The full image canvas width.</param>
        /// <param name="baseHeight">The full image canvas height.</param>
        /// <returns>A new 32-bit ARGB <see cref="Bitmap"/> with the layer positioned at (xofs, yofs).</returns>
        private static Bitmap GetBitmapFromOraLayer(int xofs, int yofs, Stream inStream, int baseWidth, int baseHeight) {
            using Bitmap layer = new(baseWidth, baseHeight, PixelFormat.Format32bppArgb);
            using Bitmap bmp = new(inStream);
            using Graphics graphics = Graphics.FromImage(layer);

            graphics.DrawImage(bmp, new Rectangle(xofs, yofs, bmp.Width, bmp.Height));

            return (Bitmap)layer.Clone();
        }

        /// <summary>
        /// Generates the XML payload for "stack.xml" specifying the OpenRaster image dimensions, resolution, and layer stack.
        /// </summary>
        /// <param name="layers">Collection of layers from the Paint.NET document.</param>
        /// <param name="info">Array of layer bounding offsets.</param>
        /// <param name="dpiX">Horizontal resolution in dots per inch.</param>
        /// <param name="dpiY">Vertical resolution in dots per inch.</param>
        /// <returns>UTF-8 encoded XML byte array conforming to OpenRaster specification v0.0.5.</returns>
        private static byte[] GetLayerXmlData(LayerList layers, LayerInfo[] info, double dpiX, double dpiY) { // OraFormat.cs - some changes
            using MemoryStream xmlStream = new();

            XmlWriterSettings settings = new() {
                Indent = true,
                OmitXmlDeclaration = false,
                ConformanceLevel = ConformanceLevel.Document,
                CloseOutput = false
            };
            XmlWriter writer = XmlWriter.Create(xmlStream, settings);

            writer.WriteStartDocument();

            writer.WriteStartElement("image");
            writer.WriteAttributeString("w", layers.GetAt(0).Width.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("h", layers.GetAt(0).Height.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("version", "0.0.5"); // mandatory

            writer.WriteAttributeString("xres", dpiX.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("yres", dpiY.ToString(CultureInfo.InvariantCulture));

            writer.WriteStartElement("stack");
            writer.WriteAttributeString("name", "root");

            // ORA stores layers top to bottom
            for (int i = layers.Count - 1; i >= 0; i--) {
                writer.WriteStartElement("layer");

                string backTile = layers[i].Metadata.GetUserValue("OraBackgroundTile");

                if (!string.IsNullOrEmpty(backTile)) {
                    writer.WriteAttributeString("background_tile", "data/background_tile.png");
                }
                string strokeMapVersion = layers[i].Metadata.GetUserValue("OraMyPaintStrokeMapVersion");

                if (!string.IsNullOrEmpty(strokeMapVersion)) {
                    writer.WriteAttributeString(strokeMapVersion, "data/layer" + i.ToString(CultureInfo.InvariantCulture) + "_strokemap.dat");
                }
                if (string.IsNullOrEmpty(strokeMapVersion)) { // the stroke map layer does not have a name
                    writer.WriteAttributeString("name", layers[i].Name);
                }

                writer.WriteAttributeString("opacity", double.Clamp(layers[i].Opacity / 255.0, 0.0, 1.0).ToString("N2", CultureInfo.InvariantCulture)); // this is even more bizarre :D

                writer.WriteAttributeString("src", "data/layer" + i.ToString(CultureInfo.InvariantCulture) + ".png");
                writer.WriteAttributeString("visibility", layers[i].Visible ? "visible" : "hidden");

                writer.WriteAttributeString("x", info[i].x.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", info[i].y.ToString(CultureInfo.InvariantCulture));

                if (blendDict.TryGetValue(layers[i].BlendMode, out string value)) {
                    writer.WriteAttributeString("composite-op", value);
                }
                else {
                    writer.WriteAttributeString("composite-op", "svg:src-over");
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // stack
            writer.WriteEndElement(); // image
            writer.WriteEndDocument();

            writer.Close();

            return xmlStream.ToArray();
        }

        /// <summary>
        /// Computes thumbnail dimensions maintaining the aspect ratio of the image within the maximum bounding box.
        /// </summary>
        /// <param name="width">Original image width.</param>
        /// <param name="height">Original image height.</param>
        /// <returns>Calculated <see cref="Size"/> for the thumbnail.</returns>
        private static Size GetThumbDimensions(int width, int height) {
            return width <= thumbMaxSize && height <= thumbMaxSize
                ? new Size(width, height)
                : width > height
                ? new Size(thumbMaxSize, (int)((double)height / width * thumbMaxSize))
                : new Size((int)((double)width / height * thumbMaxSize), thumbMaxSize);
        }

        /// <summary>
        /// Retrieves the value of an XML attribute, returning a fallback string if the attribute is absent or empty.
        /// </summary>
        /// <param name="element">The XML element to inspect.</param>
        /// <param name="attribute">The attribute name.</param>
        /// <param name="defValue">Default value to return if the attribute is missing or whitespace.</param>
        /// <returns>Attribute string or default value.</returns>
        private static string GetAttribute(XmlElement element, string attribute, string defValue) {
            string attrValue = element.GetAttribute(attribute);
            return string.IsNullOrWhiteSpace(attrValue) ? defValue : attrValue;
        }

        /// <summary>
        /// Reads all bytes from a <see cref="ZipArchiveEntry"/> and encodes them as a Base64 string.
        /// </summary>
        /// <param name="zipEntry">The zip entry to encode.</param>
        /// <returns>A Base64 string representation of the entry data.</returns>
        private static string ToBase64(ZipArchiveEntry zipEntry) {
            using Stream stream = zipEntry.Open();
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            return Convert.ToBase64String(memory.ToArray());
        }
    }

    /// <summary>
    /// FileType factory recognized by Paint.NET plugin loader to expose <see cref="OraFileType"/>.
    /// </summary>
    public class OraFileTypeFactory : IFileTypeFactory {
        /// <summary>
        /// Returns an array containing an instance of <see cref="OraFileType"/>.
        /// </summary>
        /// <returns>Array containing the OpenRaster file type handler.</returns>
        public FileType[] GetFileTypeInstances() => [new OraFileType()];
    }
}
