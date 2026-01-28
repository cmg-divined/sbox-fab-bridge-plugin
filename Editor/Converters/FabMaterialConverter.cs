using System;
using System.IO;
using System.Text;

namespace FabBridge.Converters;

/// <summary>
/// Generates s&box VMAT materials from Fab PBR texture data
/// </summary>
public static class FabMaterialConverter
{
	/// <summary>
	/// Result of a material generation
	/// </summary>
	public class ConversionResult
	{
		public bool Success { get; set; }
		public string VmatPath { get; set; }
		public string RelativePath { get; set; }
		public string Error { get; set; }
		public Asset Asset { get; set; }
	}

	/// <summary>
	/// Texture paths for material generation
	/// </summary>
	public class MaterialTextures
	{
		public string Color { get; set; }
		public string Normal { get; set; }
		public string Roughness { get; set; }
		public string Metalness { get; set; }
		public string AmbientOcclusion { get; set; }
		public string Displacement { get; set; }
		public string Opacity { get; set; }
		public string Emissive { get; set; }
	}

	/// <summary>
	/// Generate a VMAT material from converted textures
	/// </summary>
	/// <param name="materialName">Name for the material</param>
	/// <param name="textures">Texture paths (relative to project)</param>
	/// <param name="destinationFolder">Absolute path to destination folder</param>
	/// <returns>Conversion result</returns>
	public static ConversionResult Generate( string materialName, MaterialTextures textures, string destinationFolder )
	{
		var result = new ConversionResult();

		try
		{
			// Ensure destination folder exists
			Directory.CreateDirectory( destinationFolder );

			// Build the VMAT file path
			var vmatPath = Path.Combine( destinationFolder, $"{materialName}.vmat" );
			result.VmatPath = vmatPath;

			// Generate the VMAT content
			var vmatContent = GenerateVmatContent( textures );

			// Write the VMAT file
			File.WriteAllText( vmatPath, vmatContent );

			Log.Info( $"FabBridge: Created VMAT at {vmatPath}" );

			// Register the asset
			var asset = AssetSystem.RegisterFile( vmatPath );
			if ( asset != null )
			{
				result.Asset = asset;
				result.RelativePath = asset.Path;
				result.Success = true;

				Log.Info( $"FabBridge: Registered material asset at {asset.Path}" );
			}
			else
			{
				result.Error = "Failed to register VMAT asset";
			}
		}
		catch ( Exception ex )
		{
			result.Error = ex.Message;
			Log.Error( $"FabBridge: Material generation failed: {ex.Message}" );
		}

		return result;
	}

	/// <summary>
	/// Generate a material from a Fab asset and its converted textures
	/// </summary>
	public static ConversionResult GenerateFromFabAsset( FabAsset fabAsset, List<FabTextureConverter.ConversionResult> textureResults, string destinationFolder )
	{
		var materialName = fabAsset.GetSafeFileName();
		var textures = new MaterialTextures();

		// Log project path for debugging
		var project = Project.Current;
		var assetsPath = project?.GetAssetsPath() ?? "(no project)";
		Log.Info( $"FabBridge: Project assets path: {assetsPath}" );
		Log.Info( $"FabBridge: Destination folder: {destinationFolder}" );

		// Map converted textures to material slots
		foreach ( var texResult in textureResults )
		{
			if ( !texResult.Success || string.IsNullOrEmpty( texResult.DestinationPath ) )
				continue;

			// Verify the file actually exists before referencing it
			if ( !File.Exists( texResult.DestinationPath ) )
			{
				Log.Warning( $"FabBridge: Texture file missing, skipping: {texResult.DestinationPath}" );
				continue;
			}

			// Use the relative path to the SOURCE image file (not .vtex)
			// Materials can reference .jpg/.png directly and s&box will compile them
			var path = FabTextureConverter.GetRelativePath( texResult.DestinationPath );

			// Log detailed path info for debugging
			Log.Info( $"FabBridge: Texture paths:" );
			Log.Info( $"  Absolute: {texResult.DestinationPath}" );
			Log.Info( $"  Relative: {path}" );
			Log.Info( $"  File exists: {File.Exists( texResult.DestinationPath )}" );

			// Determine texture type from the destination path suffix
			var fileName = Path.GetFileNameWithoutExtension( texResult.DestinationPath )?.ToLowerInvariant() ?? "";

			if ( fileName.EndsWith( "_color" ) || fileName.EndsWith( "_albedo" ) || fileName.EndsWith( "_basecolor" ) )
				textures.Color = path;
			else if ( fileName.EndsWith( "_normal" ) )
				textures.Normal = path;
			else if ( fileName.EndsWith( "_rough" ) || fileName.EndsWith( "_roughness" ) )
				textures.Roughness = path;
			else if ( fileName.EndsWith( "_metal" ) || fileName.EndsWith( "_metalness" ) || fileName.EndsWith( "_metallic" ) )
				textures.Metalness = path;
			else if ( fileName.EndsWith( "_ao" ) || fileName.EndsWith( "_occlusion" ) || fileName.EndsWith( "_ambientocclusion" ) )
				textures.AmbientOcclusion = path;
			else if ( fileName.EndsWith( "_height" ) || fileName.EndsWith( "_displacement" ) )
				textures.Displacement = path;
			else if ( fileName.EndsWith( "_trans" ) || fileName.EndsWith( "_opacity" ) || fileName.EndsWith( "_alpha" ) )
				textures.Opacity = path;
			else if ( fileName.EndsWith( "_selfillum" ) || fileName.EndsWith( "_emissive" ) )
				textures.Emissive = path;
		}

		return Generate( materialName, textures, destinationFolder );
	}

	/// <summary>
	/// Generate the VMAT file content
	/// </summary>
	private static string GenerateVmatContent( MaterialTextures textures )
	{
		var sb = new StringBuilder();
		sb.AppendLine( "// Generated by FabBridge" );
		sb.AppendLine();
		sb.AppendLine( "Layer0" );
		sb.AppendLine( "{" );
		sb.AppendLine( "\tshader \"complex.shader\"" );
		sb.AppendLine();

		// Color/Albedo texture
		if ( !string.IsNullOrEmpty( textures.Color ) )
		{
			sb.AppendLine( "\t//---- Color ----" );
			sb.AppendLine( $"\tTextureColor \"{textures.Color}\"" );
			sb.AppendLine( "\tg_flModelTintAmount \"1.000\"" );
			sb.AppendLine( "\tg_vColorTint \"[1.000000 1.000000 1.000000 0.000000]\"" );
			sb.AppendLine();
		}
		else
		{
			sb.AppendLine( "\t//---- Color ----" );
			sb.AppendLine( "\tTextureColor \"materials/default/default_color.tga\"" );
			sb.AppendLine();
		}

		// Normal texture
		if ( !string.IsNullOrEmpty( textures.Normal ) )
		{
			sb.AppendLine( "\t//---- Normal ----" );
			sb.AppendLine( $"\tTextureNormal \"{textures.Normal}\"" );
			sb.AppendLine();
		}
		else
		{
			sb.AppendLine( "\t//---- Normal ----" );
			sb.AppendLine( "\tTextureNormal \"materials/default/default_normal.tga\"" );
			sb.AppendLine();
		}

		// Roughness texture
		if ( !string.IsNullOrEmpty( textures.Roughness ) )
		{
			sb.AppendLine( "\t//---- Roughness ----" );
			sb.AppendLine( $"\tTextureRoughness \"{textures.Roughness}\"" );
			sb.AppendLine();
		}
		else
		{
			sb.AppendLine( "\t//---- Roughness ----" );
			sb.AppendLine( "\tTextureRoughness \"materials/default/default_rough.tga\"" );
			sb.AppendLine();
		}

		// Ambient Occlusion texture
		if ( !string.IsNullOrEmpty( textures.AmbientOcclusion ) )
		{
			sb.AppendLine( "\t//---- Ambient Occlusion ----" );
			sb.AppendLine( $"\tTextureAmbientOcclusion \"{textures.AmbientOcclusion}\"" );
			sb.AppendLine( "\tg_flAmbientOcclusionDirectDiffuse \"0.000\"" );
			sb.AppendLine( "\tg_flAmbientOcclusionDirectSpecular \"0.000\"" );
			sb.AppendLine();
		}
		else
		{
			sb.AppendLine( "\t//---- Ambient Occlusion ----" );
			sb.AppendLine( "\tTextureAmbientOcclusion \"materials/default/default_ao.tga\"" );
			sb.AppendLine();
		}

		// Metalness texture (requires feature flag)
		if ( !string.IsNullOrEmpty( textures.Metalness ) )
		{
			sb.AppendLine( "\t//---- Metalness ----" );
			sb.AppendLine( "\tF_METALNESS_TEXTURE 1" );
			sb.AppendLine( "\tF_SPECULAR 1" );
			sb.AppendLine( $"\tTextureMetalness \"{textures.Metalness}\"" );
			sb.AppendLine();
		}
		else
		{
			sb.AppendLine( "\t//---- Metalness ----" );
			sb.AppendLine( "\tg_flMetalness \"0.000\"" );
			sb.AppendLine();
		}

		// Self-illumination/Emissive (requires feature flag)
		if ( !string.IsNullOrEmpty( textures.Emissive ) )
		{
			sb.AppendLine( "\t//---- Self Illum ----" );
			sb.AppendLine( "\tF_SELF_ILLUM 1" );
			sb.AppendLine( $"\tTextureSelfIllumMask \"{textures.Emissive}\"" );
			sb.AppendLine();
		}

		// Translucency/Opacity (requires feature flag)
		if ( !string.IsNullOrEmpty( textures.Opacity ) )
		{
			sb.AppendLine( "\t//---- Translucent ----" );
			sb.AppendLine( "\tF_TRANSLUCENT 1" );
			sb.AppendLine( $"\tTextureTranslucency \"{textures.Opacity}\"" );
			sb.AppendLine();
		}

		// Standard settings
		sb.AppendLine( "\t//---- Fog ----" );
		sb.AppendLine( "\tg_bFogEnabled \"1\"" );
		sb.AppendLine();

		sb.AppendLine( "\t//---- Texture Coordinates ----" );
		sb.AppendLine( "\tg_nScaleTexCoordUByModelScaleAxis \"0\"" );
		sb.AppendLine( "\tg_nScaleTexCoordVByModelScaleAxis \"0\"" );
		sb.AppendLine( "\tg_vTexCoordOffset \"[0.000 0.000]\"" );
		sb.AppendLine( "\tg_vTexCoordScale \"[1.000 1.000]\"" );
		sb.AppendLine( "\tg_vTexCoordScrollSpeed \"[0.000 0.000]\"" );

		sb.AppendLine( "}" );

		return sb.ToString();
	}
}
