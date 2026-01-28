using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FabBridge.Converters;

/// <summary>
/// Converts Fab mesh files (FBX) to s&box VMDL format
/// </summary>
public static class FabModelConverter
{
	/// <summary>
	/// Result of a model conversion
	/// </summary>
	public class ConversionResult
	{
		public bool Success { get; set; }
		public string SourcePath { get; set; }
		public string DestinationPath { get; set; }
		public string VmdlPath { get; set; }
		public string RelativePath { get; set; }
		public string Error { get; set; }
		public Asset Asset { get; set; }
	}

	/// <summary>
	/// Convert a Fab mesh to s&box VMDL format
	/// </summary>
	/// <param name="fabMesh">The Fab mesh info</param>
	/// <param name="assetBaseName">Base name for the asset</param>
	/// <param name="destinationFolder">Absolute path to destination folder (should be in models/)</param>
	/// <returns>Conversion result with paths and asset reference</returns>
	public static ConversionResult Convert( FabMesh fabMesh, string assetBaseName, string destinationFolder )
	{
		var result = new ConversionResult
		{
			SourcePath = fabMesh.Path
		};

		try
		{
			// Validate source file exists
			if ( !File.Exists( fabMesh.Path ) )
			{
				result.Error = $"Source file not found: {fabMesh.Path}";
				return result;
			}

			// Ensure destination folder exists
			Directory.CreateDirectory( destinationFolder );

			// Build destination filename
			var extension = Path.GetExtension( fabMesh.Path );
			var destFileName = $"{assetBaseName}{extension}";
			var destPath = Path.Combine( destinationFolder, destFileName );

			// Copy the mesh file
			File.Copy( fabMesh.Path, destPath, overwrite: true );
			result.DestinationPath = destPath;

			Log.Info( $"FabBridge: Copied mesh to {destPath}" );

			// Register the FBX file as an asset
			var fbxAsset = AssetSystem.RegisterFile( destPath );
			if ( fbxAsset == null )
			{
				result.Error = "Failed to register FBX asset";
				return result;
			}

			// Use EditorUtility to create VMDL from the mesh file
			var vmdlPath = Path.ChangeExtension( destPath, ".vmdl" );
			result.VmdlPath = vmdlPath;

			// This is the key function that auto-creates VMDL from FBX
			var vmdlAsset = EditorUtility.CreateModelFromMeshFile( fbxAsset, vmdlPath );

			if ( vmdlAsset != null )
			{
				result.Asset = vmdlAsset;
				result.RelativePath = vmdlAsset.Path;
				result.Success = true;

				Log.Info( $"FabBridge: Created VMDL asset at {vmdlAsset.Path}" );
			}
			else
			{
				// VMDL might already exist, try to find it
				var existingAsset = AssetSystem.FindByPath( vmdlPath );
				if ( existingAsset != null )
				{
					result.Asset = existingAsset;
					result.RelativePath = existingAsset.Path;
					result.Success = true;
					Log.Info( $"FabBridge: Found existing VMDL asset at {existingAsset.Path}" );
				}
				else
				{
					result.Error = "Failed to create VMDL asset (may already exist)";
				}
			}
		}
		catch ( Exception ex )
		{
			result.Error = ex.Message;
			Log.Error( $"FabBridge: Model conversion failed: {ex.Message}" );
		}

		return result;
	}

	/// <summary>
	/// Convert a mesh from a file path (for LODs or direct paths)
	/// </summary>
	public static ConversionResult ConvertFromPath( string sourcePath, string assetBaseName, string destinationFolder )
	{
		var fabMesh = new FabMesh
		{
			Path = sourcePath,
			Name = Path.GetFileNameWithoutExtension( sourcePath ),
			Format = Path.GetExtension( sourcePath )?.TrimStart( '.' )
		};

		return Convert( fabMesh, assetBaseName, destinationFolder );
	}

	/// <summary>
	/// Convert all meshes from a Fab asset
	/// </summary>
	public static List<ConversionResult> ConvertAllMeshes( FabAsset fabAsset, string destinationFolder )
	{
		var results = new List<ConversionResult>();
		var baseName = fabAsset.GetSafeFileName();

		// Use the new GetAllMeshes method that handles both Meshes and MeshList
		var allMeshes = fabAsset.GetAllMeshes();
		Log.Info( $"FabModelConverter: Found {allMeshes.Count} meshes to convert" );

		for ( int i = 0; i < allMeshes.Count; i++ )
		{
			var mesh = allMeshes[i];
			var meshPath = mesh.GetFilePath();
			Log.Info( $"FabModelConverter: Converting mesh {i} - Path: {meshPath}" );

			if ( string.IsNullOrEmpty( meshPath ) )
			{
				Log.Warning( $"FabModelConverter: Mesh {i} has no file path" );
				continue;
			}

			var meshName = allMeshes.Count > 1 ? $"{baseName}_{i}" : baseName;
			var result = ConvertFromPath( meshPath, meshName, destinationFolder );
			results.Add( result );
		}

		// Also handle LODs if present
		if ( fabAsset.LodList != null )
		{
			foreach ( var lod in fabAsset.LodList )
			{
				var lodPath = lod.GetFilePath();
				if ( !string.IsNullOrEmpty( lodPath ) && File.Exists( lodPath ) )
				{
					var lodName = $"{baseName}_lod{lod.Lod}";
					var result = ConvertFromPath( lodPath, lodName, destinationFolder );
					results.Add( result );
				}
			}
		}

		// Handle components that might be meshes
		if ( fabAsset.Components != null )
		{
			foreach ( var component in fabAsset.Components )
			{
				if ( IsMeshFile( component.Path ) )
				{
					var componentName = !string.IsNullOrEmpty( component.Name )
						? $"{baseName}_{component.Name.ToLowerInvariant().Replace( " ", "_" )}"
						: baseName;
					var result = ConvertFromPath( component.Path, componentName, destinationFolder );
					results.Add( result );
				}
			}
		}

		return results;
	}

	/// <summary>
	/// Check if a file path is a mesh file
	/// </summary>
	private static bool IsMeshFile( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;

		var ext = Path.GetExtension( path )?.ToLowerInvariant();
		return ext == ".fbx" || ext == ".obj" || ext == ".gltf" || ext == ".glb" || ext == ".dae";
	}

	/// <summary>
	/// Set the default material in a VMDL file
	/// </summary>
	/// <param name="vmdlPath">Absolute path to the VMDL file</param>
	/// <param name="materialPath">Project-relative path to the material (e.g., "fab_imports/asset/materials/asset.vmat")</param>
	/// <returns>True if successful</returns>
	public static bool SetDefaultMaterial( string vmdlPath, string materialPath )
	{
		try
		{
			if ( !File.Exists( vmdlPath ) )
			{
				Log.Warning( $"FabModelConverter: VMDL file not found: {vmdlPath}" );
				return false;
			}

			var content = File.ReadAllText( vmdlPath );

			// Pattern to find the global_default_material line
			var materialPattern = new Regex( @"global_default_material\s*=\s*""[^""]*""" );
			
			if ( materialPattern.IsMatch( content ) )
			{
				// Replace existing material path
				content = materialPattern.Replace( content, $"global_default_material = \"{materialPath}\"" );
				Log.Info( $"FabModelConverter: Updated global_default_material to {materialPath}" );
			}
			else
			{
				// If no global_default_material exists, we need to add the MaterialGroupList section
				// This is more complex - for now just log a warning
				Log.Warning( $"FabModelConverter: No global_default_material found in VMDL, cannot set material" );
				return false;
			}

			File.WriteAllText( vmdlPath, content );

			// The asset system should auto-detect the file change and recompile
			// Manually triggering Compile() can cause memory issues with file watchers
			Log.Info( $"FabModelConverter: Updated VMDL file, asset system will recompile automatically" );

			return true;
		}
		catch ( Exception ex )
		{
			Log.Error( $"FabModelConverter: Failed to set default material: {ex.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Set the default material for a conversion result
	/// </summary>
	public static bool SetDefaultMaterial( ConversionResult modelResult, FabMaterialConverter.ConversionResult materialResult )
	{
		if ( modelResult == null || !modelResult.Success || string.IsNullOrEmpty( modelResult.VmdlPath ) )
		{
			Log.Warning( "FabModelConverter: Invalid model result, cannot set material" );
			return false;
		}

		if ( materialResult == null || !materialResult.Success || string.IsNullOrEmpty( materialResult.RelativePath ) )
		{
			Log.Warning( "FabModelConverter: Invalid material result, cannot set material" );
			return false;
		}

		// Normalize the material path to use forward slashes
		var normalizedMaterialPath = materialResult.RelativePath?.Replace( '\\', '/' );
		return SetDefaultMaterial( modelResult.VmdlPath, normalizedMaterialPath );
	}
}
