using System;
using System.IO;
using System.Text.Json;
using Editor.TextureEditor;

namespace FabBridge.Converters;

/// <summary>
/// Converts Fab texture files to s&box VTEX format
/// </summary>
public static class FabTextureConverter
{
	/// <summary>
	/// Result of a texture conversion
	/// </summary>
	public class ConversionResult
	{
		public bool Success { get; set; }
		public string SourcePath { get; set; }
		public string DestinationPath { get; set; }
		public string VtexPath { get; set; }
		public string RelativePath { get; set; }
		public string Error { get; set; }
		public Asset Asset { get; set; }
	}

	/// <summary>
	/// Convert a Fab texture to s&box format
	/// </summary>
	/// <param name="fabTexture">The Fab texture info</param>
	/// <param name="assetBaseName">Base name for the asset (e.g., material name)</param>
	/// <param name="destinationFolder">Absolute path to destination folder</param>
	/// <returns>Conversion result with paths and asset reference</returns>
	public static ConversionResult Convert( FabTexture fabTexture, string assetBaseName, string destinationFolder )
	{
		var result = new ConversionResult
		{
			SourcePath = fabTexture.Path
		};

		try
		{
			// Validate source file exists
			if ( !File.Exists( fabTexture.Path ) )
			{
				result.Error = $"Source file not found: {fabTexture.Path}";
				return result;
			}

			// Ensure destination folder exists
			Directory.CreateDirectory( destinationFolder );

			// Build destination filename with proper suffix
			var suffix = fabTexture.GetSboxSuffix();
			var extension = Path.GetExtension( fabTexture.Path );
			var destFileName = $"{assetBaseName}{suffix}{extension}";
			var destPath = Path.Combine( destinationFolder, destFileName );

			// Copy the texture file
			File.Copy( fabTexture.Path, destPath, overwrite: true );
			result.DestinationPath = destPath;

			// Verify the file was actually copied
			if ( !File.Exists( destPath ) )
			{
				result.Error = $"File copy succeeded but file not found at destination: {destPath}";
				Log.Error( $"FabBridge: {result.Error}" );
				return result;
			}

			var fileInfo = new FileInfo( destPath );
			Log.Info( $"FabBridge: Copied texture to {destPath} (size: {fileInfo.Length} bytes)" );

			// Store the relative path to the IMAGE file (not vtex)
			result.RelativePath = GetRelativePath( destPath );
			result.Success = true;

			Log.Info( $"FabBridge: Texture relative path: {result.RelativePath}" );

			// Optionally create VTEX file for compilation (but material can reference jpg directly)
			// Commenting out VTEX creation for now as it may be causing issues
			// var vtexPath = Path.ChangeExtension( destPath, ".vtex" );
			// CreateVtexFile( destPath, vtexPath, fabTexture );
			// result.VtexPath = vtexPath;
		}
		catch ( Exception ex )
		{
			result.Error = ex.Message;
			Log.Error( $"FabBridge: Texture conversion failed: {ex.Message}" );
		}

		return result;
	}

	/// <summary>
	/// Convert multiple textures from a texture set
	/// </summary>
	public static List<ConversionResult> ConvertTextureSet( FabTextureSet textureSet, string assetBaseName, string destinationFolder )
	{
		var results = new List<ConversionResult>();

		foreach ( var texture in textureSet.Textures )
		{
			var result = Convert( texture, assetBaseName, destinationFolder );
			results.Add( result );
		}

		return results;
	}

	/// <summary>
	/// Convert all textures from a Fab asset
	/// </summary>
	public static List<ConversionResult> ConvertAllTextures( FabAsset fabAsset, string destinationFolder )
	{
		var results = new List<ConversionResult>();
		var baseName = fabAsset.GetSafeFileName();

		// Use the new GetAllTextures method that handles all formats
		var allTextures = fabAsset.GetAllTextures();
		Log.Info( $"FabTextureConverter: Found {allTextures.Count} textures to convert" );

		foreach ( var texture in allTextures )
		{
			Log.Info( $"FabTextureConverter: Converting texture - Type: {texture.Type}, Path: {texture.Path}" );
			var result = Convert( texture, baseName, destinationFolder );
			results.Add( result );
		}

		return results;
	}

	/// <summary>
	/// Creates a VTEX file for the texture
	/// </summary>
	private static void CreateVtexFile( string imagePath, string vtexPath, FabTexture fabTexture )
	{
		// Get the relative path for the image (normalized to forward slashes)
		var relativePath = GetRelativePath( imagePath );

		// Determine if we need linear color space (for normal/roughness/etc)
		var isLinear = fabTexture.IsLinearColorSpace();

		// Create the texture file definition
		var textureFile = new TextureFile
		{
			Sequences = new List<TextureSequence>
			{
				new TextureSequence
				{
					Source = relativePath,
					IsLooping = true
				}
			},
			InputColorSpace = isLinear ? GammaType.Linear : GammaType.SRGB,
			OutputColorSpace = GammaType.Linear,
			OutputFormat = ImageFormatType.BC7, // High quality compression
			OutputMipAlgorithm = MipAlgorithm.Box,
			OutputTypeString = "2D"
		};

		// Serialize and write
		var json = Json.Serialize( textureFile );
		File.WriteAllText( vtexPath, json );

		Log.Info( $"FabBridge: Created VTEX file at {vtexPath}" );
	}

	/// <summary>
	/// Gets the project-relative path for a texture (normalized to forward slashes)
	/// </summary>
	public static string GetRelativePath( string absolutePath )
	{
		var project = Project.Current;
		if ( project == null )
		{
			Log.Warning( "FabBridge: GetRelativePath - No project, returning normalized absolute path" );
			return NormalizePath( absolutePath );
		}

		var projectPath = project.GetAssetsPath();
		
		// Normalize both paths for comparison
		var normalizedAbsolute = absolutePath.Replace( '\\', '/' );
		var normalizedProject = projectPath.Replace( '\\', '/' );
		
		// Ensure project path ends with /
		if ( !normalizedProject.EndsWith( "/" ) )
			normalizedProject += "/";
		
		Log.Info( $"FabBridge: GetRelativePath debug:" );
		Log.Info( $"  Input: {absolutePath}" );
		Log.Info( $"  Normalized absolute: {normalizedAbsolute}" );
		Log.Info( $"  Normalized project: {normalizedProject}" );
		
		if ( normalizedAbsolute.StartsWith( normalizedProject, StringComparison.OrdinalIgnoreCase ) )
		{
			var relativePath = normalizedAbsolute.Substring( normalizedProject.Length );
			Log.Info( $"  Result: {relativePath}" );
			return relativePath;
		}

		Log.Warning( $"FabBridge: Path not under project, returning normalized: {normalizedAbsolute}" );
		return NormalizePath( absolutePath );
	}

	/// <summary>
	/// Normalize path to use forward slashes (s&box convention)
	/// </summary>
	public static string NormalizePath( string path )
	{
		return path?.Replace( '\\', '/' );
	}
}
