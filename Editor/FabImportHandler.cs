using System;
using System.IO;
using FabBridge.Converters;

namespace FabBridge;

/// <summary>
/// Handles the complete import process for Fab assets
/// </summary>
public class FabImportHandler
{
	/// <summary>
	/// Result of a complete asset import
	/// </summary>
	public class ImportResult
	{
		public bool Success { get; set; }
		public string AssetName { get; set; }
		public List<FabTextureConverter.ConversionResult> TextureResults { get; set; } = new();
		public List<FabModelConverter.ConversionResult> ModelResults { get; set; } = new();
		public FabMaterialConverter.ConversionResult MaterialResult { get; set; }
		public List<string> Errors { get; set; } = new();
		public DateTime ImportTime { get; set; }
	}

	/// <summary>
	/// Event raised when an import starts
	/// </summary>
	public event Action<FabAsset> OnImportStarted;

	/// <summary>
	/// Event raised when an import completes
	/// </summary>
	public event Action<ImportResult> OnImportCompleted;

	/// <summary>
	/// Event raised for progress updates
	/// </summary>
	public event Action<string> OnProgressUpdate;

	/// <summary>
	/// The base folder for imported assets (relative to project assets)
	/// </summary>
	public string ImportFolder { get; set; } = "fab_imports";

	/// <summary>
	/// Whether to automatically create materials
	/// </summary>
	public bool CreateMaterials { get; set; } = true;

	/// <summary>
	/// Whether to automatically convert models
	/// </summary>
	public bool ConvertModels { get; set; } = true;

	/// <summary>
	/// Import all assets from an export data package
	/// </summary>
	public async Task<List<ImportResult>> ImportAllAsync( FabExportData exportData )
	{
		var results = new List<ImportResult>();

		foreach ( var asset in exportData.Assets )
		{
			var result = await ImportAssetAsync( asset );
			results.Add( result );
		}

		return results;
	}

	/// <summary>
	/// Import a single Fab asset
	/// </summary>
	public async Task<ImportResult> ImportAssetAsync( FabAsset fabAsset )
	{
		var result = new ImportResult
		{
			AssetName = fabAsset.Name ?? fabAsset.Id ?? "Unknown",
			ImportTime = DateTime.Now
		};

		// Debug logging for the asset
		Log.Info( $"FabBridge: === Starting import ===" );
		Log.Info( $"FabBridge: Asset ID: {fabAsset.Id}" );
		Log.Info( $"FabBridge: Asset Name: {fabAsset.Name}" );
		Log.Info( $"FabBridge: Display Name: {fabAsset.GetDisplayName()}" );
		Log.Info( $"FabBridge: Asset Type: {fabAsset.Type}" );
		Log.Info( $"FabBridge: Asset Category: {fabAsset.Category}" );
		Log.Info( $"FabBridge: Asset Path: {fabAsset.Path}" );

		// Use the new aggregated methods
		var allMeshes = fabAsset.GetAllMeshes();
		var allTextures = fabAsset.GetAllTextures();

		Log.Info( $"FabBridge: Total meshes (from GetAllMeshes): {allMeshes.Count}" );
		Log.Info( $"FabBridge: Total textures (from GetAllTextures): {allTextures.Count}" );
		Log.Info( $"FabBridge: Meshes array count: {fabAsset.Meshes?.Count ?? 0}" );
		Log.Info( $"FabBridge: MeshList array count: {fabAsset.MeshList?.Count ?? 0}" );
		Log.Info( $"FabBridge: Materials array count: {fabAsset.Materials?.Count ?? 0}" );
		Log.Info( $"FabBridge: Components count: {fabAsset.Components?.Count ?? 0}" );

		// Log mesh details
		foreach ( var mesh in allMeshes )
		{
			Log.Info( $"FabBridge: Mesh - Name: {mesh.Name}, File: {mesh.GetFilePath()}" );
		}

		// Log texture details
		foreach ( var tex in allTextures )
		{
			Log.Info( $"FabBridge: Texture - Type: {tex.Type}, Path: {tex.Path}" );
		}

		// Log materials info
		if ( fabAsset.Materials != null )
		{
			foreach ( var mat in fabAsset.Materials )
			{
				var textureCount = mat.Textures?.Count ?? 0;
				var textureTypes = mat.Textures != null ? string.Join( ", ", mat.Textures.Keys ) : "none";
				Log.Info( $"FabBridge: Material - Name: {mat.Name}, Textures: {textureCount} ({textureTypes})" );
			}
		}

		try
		{
			OnImportStarted?.Invoke( fabAsset );
			OnProgressUpdate?.Invoke( $"Importing {result.AssetName}..." );

			// Get the project assets path
			var project = Project.Current;
			if ( project == null )
			{
				Log.Error( "FabBridge: No project loaded!" );
				result.Errors.Add( "No project loaded" );
				return result;
			}

			var assetsPath = project.GetAssetsPath();
			Log.Info( $"FabBridge: Project assets path: {assetsPath}" );

			var assetBaseName = fabAsset.GetSafeFileName();
			Log.Info( $"FabBridge: Safe file name: {assetBaseName}" );

			// Create import folders
			var importBasePath = Path.Combine( assetsPath, ImportFolder, assetBaseName );
			var materialsPath = Path.Combine( importBasePath, "materials" );
			var modelsPath = Path.Combine( importBasePath, "models" );

			Log.Info( $"FabBridge: Import base path: {importBasePath}" );
			Log.Info( $"FabBridge: Materials path: {materialsPath}" );
			Log.Info( $"FabBridge: Models path: {modelsPath}" );

			Directory.CreateDirectory( materialsPath );
			Directory.CreateDirectory( modelsPath );
			Log.Info( "FabBridge: Created directories" );

			// Step 1: Import textures
			OnProgressUpdate?.Invoke( $"Converting textures for {result.AssetName}..." );
			await Task.Yield(); // Allow UI to update

			Log.Info( "FabBridge: Starting texture conversion..." );
			result.TextureResults = FabTextureConverter.ConvertAllTextures( fabAsset, materialsPath );
			Log.Info( $"FabBridge: Texture conversion complete. Results: {result.TextureResults.Count}" );

			foreach ( var texResult in result.TextureResults )
			{
				Log.Info( $"FabBridge: Texture result - Success: {texResult.Success}, Path: {texResult.DestinationPath}, Error: {texResult.Error}" );
				if ( !texResult.Success && !string.IsNullOrEmpty( texResult.Error ) )
				{
					result.Errors.Add( $"Texture: {texResult.Error}" );
				}
			}

			// Step 2: Create material if we have textures
			var hasSuccessfulTextures = result.TextureResults.Any( t => t.Success );
			Log.Info( $"FabBridge: CreateMaterials={CreateMaterials}, hasSuccessfulTextures={hasSuccessfulTextures}" );

			if ( CreateMaterials && hasSuccessfulTextures )
			{
				OnProgressUpdate?.Invoke( $"Creating material for {result.AssetName}..." );
				await Task.Yield();

				Log.Info( "FabBridge: Starting material generation..." );
				result.MaterialResult = FabMaterialConverter.GenerateFromFabAsset(
					fabAsset,
					result.TextureResults,
					materialsPath
				);
				Log.Info( $"FabBridge: Material result - Success: {result.MaterialResult?.Success}, Path: {result.MaterialResult?.VmatPath}, Error: {result.MaterialResult?.Error}" );

				if ( !result.MaterialResult.Success && !string.IsNullOrEmpty( result.MaterialResult.Error ) )
				{
					result.Errors.Add( $"Material: {result.MaterialResult.Error}" );
				}
			}

			// Step 3: Import models
			var meshesToConvert = fabAsset.GetAllMeshes();
			var hasMeshes = meshesToConvert.Count > 0;
			var hasLods = fabAsset.LodList?.Count > 0;
			var hasMeshComponents = fabAsset.Components?.Any( c => IsMeshFile( c.Path ) ) ?? false;
			Log.Info( $"FabBridge: ConvertModels={ConvertModels}, hasMeshes={hasMeshes} (count={meshesToConvert.Count}), hasLods={hasLods}, hasMeshComponents={hasMeshComponents}" );

			if ( ConvertModels && (hasMeshes || hasLods || hasMeshComponents) )
			{
				OnProgressUpdate?.Invoke( $"Converting models for {result.AssetName}..." );
				await Task.Yield();

				Log.Info( "FabBridge: Starting model conversion..." );
				result.ModelResults = FabModelConverter.ConvertAllMeshes( fabAsset, modelsPath );
				Log.Info( $"FabBridge: Model conversion complete. Results: {result.ModelResults.Count}" );

				foreach ( var modelResult in result.ModelResults )
				{
					Log.Info( $"FabBridge: Model result - Success: {modelResult.Success}, Path: {modelResult.VmdlPath}, Error: {modelResult.Error}" );
					if ( !modelResult.Success && !string.IsNullOrEmpty( modelResult.Error ) )
					{
						result.Errors.Add( $"Model: {modelResult.Error}" );
					}
				}

				// Step 4: Set default material on models
				if ( result.MaterialResult?.Success == true && result.ModelResults.Any( m => m.Success ) )
				{
					OnProgressUpdate?.Invoke( $"Assigning material to models..." );
					await Task.Yield();

					Log.Info( "FabBridge: Assigning default material to models..." );
					foreach ( var modelResult in result.ModelResults.Where( m => m.Success ) )
					{
						var materialSet = FabModelConverter.SetDefaultMaterial( modelResult, result.MaterialResult );
						Log.Info( $"FabBridge: Set material on {modelResult.VmdlPath}: {materialSet}" );
					}
				}
			}

			// Determine overall success
			result.Success = result.TextureResults.Any( t => t.Success ) ||
			                 result.ModelResults.Any( m => m.Success ) ||
			                 (result.MaterialResult?.Success ?? false);

			Log.Info( $"FabBridge: === Import complete ===" );
			Log.Info( $"FabBridge: Success: {result.Success}" );
			Log.Info( $"FabBridge: Errors: {string.Join( ", ", result.Errors )}" );

			OnProgressUpdate?.Invoke( result.Success
				? $"Successfully imported {result.AssetName}"
				: $"Failed to import {result.AssetName}" );
		}
		catch ( Exception ex )
		{
			result.Errors.Add( ex.Message );
			Log.Error( $"FabBridge: Import failed for {result.AssetName}: {ex.Message}" );
			Log.Error( $"FabBridge: Stack trace: {ex.StackTrace}" );
		}

		OnImportCompleted?.Invoke( result );
		return result;
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
}
