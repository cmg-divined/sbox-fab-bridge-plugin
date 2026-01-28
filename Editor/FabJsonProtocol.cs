using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FabBridge;

/// <summary>
/// Handles parsing of JSON data from Fab/Quixel Bridge
/// </summary>
public static class FabJsonProtocol
{
	/// <summary>
	/// Whether to save incoming JSON to files for debugging
	/// </summary>
	public static bool SaveJsonToFile { get; set; } = false;

	/// <summary>
	/// Parse the JSON export data from Fab
	/// </summary>
	public static FabExportData Parse( string json )
	{
		if ( string.IsNullOrWhiteSpace( json ) )
			return null;

		try
		{
			// Save full JSON to file for debugging
			if ( SaveJsonToFile )
			{
				SaveJsonForDebugging( json );
			}

			// Log first 500 chars of JSON for debugging
			var preview = json.Length > 500 ? json.Substring( 0, 500 ) + "..." : json;
			Log.Info( $"FabJsonProtocol: Parsing JSON preview: {preview}" );

			// Try direct deserialization first
			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};

			// Check if it's wrapped in an outer object or is an array
			var trimmed = json.Trim();
			Log.Info( $"FabJsonProtocol: JSON starts with: {trimmed.Substring( 0, Math.Min( 50, trimmed.Length ) )}" );

			if ( trimmed.StartsWith( "[" ) )
			{
				Log.Info( "FabJsonProtocol: Detected array format" );
				// It's an array of assets
				var assets = JsonSerializer.Deserialize<List<FabAsset>>( json, options );
				return new FabExportData { Assets = assets ?? new() };
			}

			// Try parsing as a generic object to inspect structure
			var node = JsonNode.Parse( json );
			if ( node == null )
				return null;

			// Log top-level keys
			if ( node is JsonObject jsonObj )
			{
				var keys = string.Join( ", ", jsonObj.Select( x => x.Key ) );
				Log.Info( $"FabJsonProtocol: Top-level keys: {keys}" );
			}

			// Check common wrapper formats
			// Format 1: { "assets": [...] }
			if ( node["assets"] is JsonArray assetsArray )
			{
				Log.Info( "FabJsonProtocol: Found 'assets' array wrapper" );
				var assets = assetsArray.Deserialize<List<FabAsset>>( options );
				return new FabExportData { Assets = assets ?? new() };
			}

			// Format 2: Single asset object (Fab direct export format)
			// Check for "id" key which is present in Fab exports
			if ( node["id"] != null || node["meshes"] != null || node["materials"] != null )
			{
				Log.Info( "FabJsonProtocol: Detected single asset object format (Fab direct export)" );
				try
				{
					var asset = node.Deserialize<FabAsset>( options );
					if ( asset != null )
					{
						Log.Info( $"FabJsonProtocol: Successfully parsed asset - ID: {asset.Id}, Name: {asset.GetDisplayName()}" );
						Log.Info( $"FabJsonProtocol: Meshes: {asset.Meshes?.Count ?? 0}, Materials: {asset.Materials?.Count ?? 0}" );
						return new FabExportData { Assets = new List<FabAsset> { asset } };
					}
				}
				catch ( Exception parseEx )
				{
					Log.Error( $"FabJsonProtocol: Failed to parse single asset: {parseEx.Message}" );
					// Continue to try other formats
				}
			}

			// Format 3: Quixel Bridge format with numbered indices or other wrappers
			var exportData = new FabExportData();
			foreach ( var prop in node.AsObject() )
			{
				// Try to parse each property as an asset
				try
				{
					var asset = prop.Value.Deserialize<FabAsset>( options );
					if ( asset != null && (!string.IsNullOrEmpty( asset.Id ) || !string.IsNullOrEmpty( asset.Name )) )
					{
						exportData.Assets.Add( asset );
					}
				}
				catch
				{
					// Not an asset, skip
				}
			}

			if ( exportData.Assets.Count > 0 )
				return exportData;

			Log.Warning( $"FabJsonProtocol: Could not parse JSON structure" );
			return null;
		}
		catch ( Exception ex )
		{
			Log.Error( $"FabJsonProtocol: Parse error: {ex.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Try to extract texture information from various JSON formats
	/// </summary>
	public static List<FabTexture> ExtractTextures( JsonNode node )
	{
		var textures = new List<FabTexture>();

		if ( node == null )
			return textures;

		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		// Check for textureSets array
		if ( node["textureSets"] is JsonArray textureSets )
		{
			foreach ( var set in textureSets )
			{
				if ( set?["textures"] is JsonArray setTextures )
				{
					foreach ( var tex in setTextures )
					{
						var texture = tex.Deserialize<FabTexture>( options );
						if ( texture != null )
							textures.Add( texture );
					}
				}
			}
		}

		// Check for components with textures
		if ( node["components"] is JsonArray components )
		{
			foreach ( var comp in components )
			{
				var path = comp?["path"]?.GetValue<string>();
				var type = comp?["type"]?.GetValue<string>();

				if ( !string.IsNullOrEmpty( path ) && IsTextureFile( path ) )
				{
					textures.Add( new FabTexture
					{
						Path = path,
						Type = type ?? GuessTextureType( path ),
						Name = System.IO.Path.GetFileNameWithoutExtension( path )
					} );
				}
			}
		}

		// Check for direct texture properties (Megascans format)
		var textureTypes = new[] { "albedo", "normal", "roughness", "metalness", "ao", "displacement", "opacity" };
		foreach ( var type in textureTypes )
		{
			var texNode = node[type] ?? node[type.ToUpperInvariant()];
			if ( texNode is JsonArray texArray )
			{
				foreach ( var tex in texArray )
				{
					var path = tex?["path"]?.GetValue<string>() ?? tex?.GetValue<string>();
					if ( !string.IsNullOrEmpty( path ) )
					{
						textures.Add( new FabTexture
						{
							Path = path,
							Type = type,
							Name = System.IO.Path.GetFileNameWithoutExtension( path )
						} );
					}
				}
			}
			else if ( texNode != null )
			{
				var path = texNode["path"]?.GetValue<string>() ?? texNode.GetValue<string>();
				if ( !string.IsNullOrEmpty( path ) )
				{
					textures.Add( new FabTexture
					{
						Path = path,
						Type = type,
						Name = System.IO.Path.GetFileNameWithoutExtension( path )
					} );
				}
			}
		}

		return textures;
	}

	private static bool IsTextureFile( string path )
	{
		var ext = System.IO.Path.GetExtension( path )?.ToLowerInvariant();
		return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" ||
		       ext == ".exr" || ext == ".tif" || ext == ".tiff" || ext == ".bmp";
	}

	private static string GuessTextureType( string path )
	{
		var name = System.IO.Path.GetFileNameWithoutExtension( path )?.ToLowerInvariant() ?? "";

		if ( name.Contains( "albedo" ) || name.Contains( "diffuse" ) || name.Contains( "color" ) || name.Contains( "basecolor" ) )
			return "albedo";
		if ( name.Contains( "normal" ) || name.Contains( "nrm" ) )
			return "normal";
		if ( name.Contains( "rough" ) )
			return "roughness";
		if ( name.Contains( "metal" ) )
			return "metalness";
		if ( name.Contains( "ao" ) || name.Contains( "occlusion" ) )
			return "ao";
		if ( name.Contains( "height" ) || name.Contains( "disp" ) )
			return "displacement";
		if ( name.Contains( "opacity" ) || name.Contains( "alpha" ) )
			return "opacity";
		if ( name.Contains( "emissive" ) || name.Contains( "glow" ) )
			return "emissive";

		return "unknown";
	}

	/// <summary>
	/// Saves the raw JSON to a file for debugging purposes
	/// </summary>
	private static void SaveJsonForDebugging( string json )
	{
		try
		{
			var project = Project.Current;
			if ( project == null )
				return;

			var assetsPath = project.GetAssetsPath();
			var debugFolder = System.IO.Path.Combine( assetsPath, "fab_imports", "_debug" );
			System.IO.Directory.CreateDirectory( debugFolder );

			var timestamp = DateTime.Now.ToString( "yyyy-MM-dd_HH-mm-ss" );
			var filename = $"fab_export_{timestamp}.json";
			var filepath = System.IO.Path.Combine( debugFolder, filename );

			// Pretty print the JSON for readability
			try
			{
				var parsed = JsonNode.Parse( json );
				var prettyJson = parsed?.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) ?? json;
				System.IO.File.WriteAllText( filepath, prettyJson );
			}
			catch
			{
				// If pretty printing fails, just save the raw JSON
				System.IO.File.WriteAllText( filepath, json );
			}

			Log.Info( $"FabJsonProtocol: Saved JSON to {filepath}" );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"FabJsonProtocol: Failed to save JSON for debugging: {ex.Message}" );
		}
	}
}
