using System;
using System.Text.Json.Serialization;

namespace FabBridge;

/// <summary>
/// Represents the root JSON structure received from Fab/Quixel Bridge
/// </summary>
public class FabExportData
{
	[JsonPropertyName( "assets" )]
	public List<FabAsset> Assets { get; set; } = new();
}

/// <summary>
/// Represents a single asset exported from Fab
/// The actual Fab JSON format from Epic Games Launcher
/// </summary>
public class FabAsset
{
	[JsonPropertyName( "id" )]
	public string Id { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "type" )]
	public string Type { get; set; }

	[JsonPropertyName( "category" )]
	public string Category { get; set; }

	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	// Fab uses "meshes" not "meshList"
	[JsonPropertyName( "meshes" )]
	public List<FabMesh> Meshes { get; set; } = new();

	// Fab uses "materials" array containing texture info
	[JsonPropertyName( "materials" )]
	public List<FabMaterial> Materials { get; set; } = new();

	// Components array (Quixel Bridge format) - textures with type/path/format
	[JsonPropertyName( "components" )]
	public List<FabComponent> Components { get; set; } = new();

	// Additional textures (list of file paths)
	[JsonPropertyName( "additional_textures" )]
	public List<string> AdditionalTextures { get; set; } = new();

	// Native files
	[JsonPropertyName( "native_files" )]
	public List<FabNativeFile> NativeFiles { get; set; } = new();

	// Metadata (nested object with launcher, megascans, fab sections)
	[JsonPropertyName( "metadata" )]
	public FabMetadata Metadata { get; set; }

	// Legacy formats for compatibility
	[JsonPropertyName( "meshList" )]
	public List<FabMesh> MeshList { get; set; } = new();

	[JsonPropertyName( "textureSets" )]
	public List<FabTextureSet> TextureSets { get; set; } = new();

	[JsonPropertyName( "lodList" )]
	public List<FabLod> LodList { get; set; } = new();

	[JsonPropertyName( "meta" )]
	public List<FabMeta> Meta { get; set; } = new();

	/// <summary>
	/// Gets all meshes from either Meshes or MeshList
	/// </summary>
	public List<FabMesh> GetAllMeshes()
	{
		var result = new List<FabMesh>();
		if ( Meshes?.Count > 0 ) result.AddRange( Meshes );
		if ( MeshList?.Count > 0 ) result.AddRange( MeshList );
		return result;
	}

	/// <summary>
	/// Gets all textures from Components, Materials, and AdditionalTextures
	/// </summary>
	public List<FabTexture> GetAllTextures()
	{
		var result = new List<FabTexture>();

		// Add from Components (Quixel Bridge format)
		if ( Components != null )
		{
			foreach ( var comp in Components )
			{
				if ( IsTextureFile( comp.Path ) )
				{
					result.Add( new FabTexture
					{
						Path = comp.Path,
						Name = comp.Name,
						Type = comp.Type,
						Format = comp.Format
					} );
				}
			}
		}

		// Add from Materials (Fab format - textures is now a Dictionary<string, string>)
		if ( Materials != null )
		{
			foreach ( var mat in Materials )
			{
				if ( mat.Textures != null )
				{
					foreach ( var kvp in mat.Textures )
					{
						var textureType = kvp.Key;   // e.g., "albedo", "normal", "roughness"
						var texturePath = kvp.Value; // e.g., "C:/path/to/texture.jpg"

						if ( !string.IsNullOrEmpty( texturePath ) && IsTextureFile( texturePath ) )
						{
							result.Add( new FabTexture
							{
								Path = texturePath,
								Type = textureType,
								Name = System.IO.Path.GetFileNameWithoutExtension( texturePath )
							} );
						}
					}
				}
			}
		}

		// Add from AdditionalTextures (list of file paths as strings)
		if ( AdditionalTextures != null )
		{
			foreach ( var texturePath in AdditionalTextures )
			{
				if ( !string.IsNullOrEmpty( texturePath ) && IsTextureFile( texturePath ) )
				{
					// Try to infer type from filename
					var filename = System.IO.Path.GetFileNameWithoutExtension( texturePath ).ToLowerInvariant();
					var type = InferTextureType( filename );

					result.Add( new FabTexture
					{
						Path = texturePath,
						Type = type,
						Name = System.IO.Path.GetFileNameWithoutExtension( texturePath )
					} );
				}
			}
		}

		// Add from TextureSets (legacy format)
		if ( TextureSets != null )
		{
			foreach ( var set in TextureSets )
			{
				if ( set.Textures != null )
				{
					result.AddRange( set.Textures );
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Infers texture type from filename
	/// </summary>
	private static string InferTextureType( string filename )
	{
		if ( filename.Contains( "basecolor" ) || filename.Contains( "albedo" ) || filename.Contains( "diffuse" ) )
			return "albedo";
		if ( filename.Contains( "normal" ) )
			return "normal";
		if ( filename.Contains( "roughness" ) )
			return "roughness";
		if ( filename.Contains( "metallic" ) || filename.Contains( "metalness" ) )
			return "metalness";
		if ( filename.Contains( "ao" ) || filename.Contains( "ambient" ) )
			return "ao";
		if ( filename.Contains( "displacement" ) || filename.Contains( "height" ) )
			return "displacement";
		if ( filename.Contains( "cavity" ) )
			return "cavity";
		if ( filename.Contains( "gloss" ) )
			return "gloss";
		if ( filename.Contains( "specular" ) )
			return "specular";
		if ( filename.Contains( "bump" ) )
			return "bump";
		// Translucency takes priority over opacity - it's the correct format for s&box
		if ( filename.Contains( "translucency" ) )
			return "translucency";
		if ( filename.Contains( "opacity" ) || filename.Contains( "alpha" ) )
			return "opacity";
		if ( filename.Contains( "emissive" ) )
			return "emissive";
		// Check occlusion last since "occlusion" could match "ambientocclusion"
		if ( filename.Contains( "occlusion" ) )
			return "ao";

		return "unknown";
	}

	private static bool IsTextureFile( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;
		var ext = System.IO.Path.GetExtension( path )?.ToLowerInvariant();
		return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" ||
		       ext == ".exr" || ext == ".tif" || ext == ".tiff" || ext == ".bmp";
	}

	/// <summary>
	/// Gets the display name for this asset
	/// </summary>
	public string GetDisplayName()
	{
		// Try to get name from various sources

		// 1. Direct Name property
		if ( !string.IsNullOrEmpty( Name ) )
			return Name;

		// 2. Megascans metadata name
		if ( !string.IsNullOrEmpty( Metadata?.Megascans?.Name ) )
			return Metadata.Megascans.Name;

		// 3. Fab listing title
		if ( !string.IsNullOrEmpty( Metadata?.Fab?.Listing?.Title ) )
			return Metadata.Fab.Listing.Title;

		// 4. Try to extract from path
		if ( !string.IsNullOrEmpty( Path ) )
		{
			var dirName = System.IO.Path.GetFileName( Path );
			if ( !string.IsNullOrEmpty( dirName ) )
				return dirName;
		}

		return Id ?? "Unknown";
	}

	/// <summary>
	/// Gets the display name for this asset (sanitized for file names)
	/// </summary>
	public string GetSafeFileName()
	{
		var name = GetDisplayName();
		// Remove invalid characters
		foreach ( var c in System.IO.Path.GetInvalidFileNameChars() )
		{
			name = name.Replace( c, '_' );
		}
		return name.ToLowerInvariant().Replace( " ", "_" );
	}
}

/// <summary>
/// Represents a mesh/model file in the export (Fab format)
/// </summary>
public class FabMesh
{
	// Fab uses "file" for the path
	[JsonPropertyName( "file" )]
	public string File { get; set; }

	// Legacy "path" property
	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "type" )]
	public string Type { get; set; }

	[JsonPropertyName( "format" )]
	public string Format { get; set; }

	[JsonPropertyName( "material_index" )]
	public int MaterialIndex { get; set; }

	[JsonPropertyName( "lods" )]
	public List<FabLod> Lods { get; set; } = new();

	/// <summary>
	/// Gets the actual file path (from File or Path property)
	/// </summary>
	public string GetFilePath() => !string.IsNullOrEmpty( File ) ? File : Path;
}

/// <summary>
/// Represents a material definition from Fab (contains texture paths)
/// </summary>
public class FabMaterial
{
	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "file" )]
	public string File { get; set; }

	[JsonPropertyName( "flipnmapgreenchannel" )]
	public bool FlipNormalY { get; set; }

	// Textures dictionary: maps texture type (albedo, normal, roughness, etc.) to file path
	[JsonPropertyName( "textures" )]
	public Dictionary<string, string> Textures { get; set; } = new();

	/// <summary>
	/// Gets texture path by type name
	/// </summary>
	public string GetTexturePath( string type )
	{
		if ( Textures != null && Textures.TryGetValue( type.ToLowerInvariant(), out var path ) )
			return path;
		return null;
	}
}

/// <summary>
/// Represents a set of PBR textures (legacy format)
/// </summary>
public class FabTextureSet
{
	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "textures" )]
	public List<FabTexture> Textures { get; set; } = new();
}

/// <summary>
/// Represents a single texture map
/// </summary>
public class FabTexture
{
	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "type" )]
	public string Type { get; set; }

	[JsonPropertyName( "resolution" )]
	public string Resolution { get; set; }

	[JsonPropertyName( "format" )]
	public string Format { get; set; }

	/// <summary>
	/// Maps Fab texture types to s&box naming conventions
	/// </summary>
	public string GetSboxSuffix()
	{
		return Type?.ToLowerInvariant() switch
		{
			"albedo" => "_color",
			"diffuse" => "_color",
			"basecolor" => "_color",
			"normal" => "_normal",
			"roughness" => "_rough",
			"metalness" => "_metal",
			"metallic" => "_metal",
			"ao" => "_ao",
			"occlusion" => "_occlusion",
			"ambientocclusion" => "_ao",
			"displacement" => "_height",
			"height" => "_height",
			"translucency" => "_translucency",
			"opacity" => "_opacity",
			"emissive" => "_selfillum",
			"mask" => "_mask",
			"bump" => "_bump",
			"gloss" => "_gloss",
			"glossiness" => "_gloss",
			"specular" => "_specular",
			"cavity" => "_cavity",
			_ => $"_{Type?.ToLowerInvariant() ?? "unknown"}"
		};
	}

	/// <summary>
	/// Determines if this texture type requires linear color space
	/// </summary>
	public bool IsLinearColorSpace()
	{
		var type = Type?.ToLowerInvariant();
		// Normal, roughness, metalness, AO, displacement are linear data
		return type == "normal" || type == "roughness" || type == "metalness" ||
		       type == "metallic" || type == "ao" || type == "ambientocclusion" ||
		       type == "displacement" || type == "height" || type == "mask";
	}
}

/// <summary>
/// Represents a component/variation of an asset (Quixel Bridge format)
/// Used for textures in Quixel Bridge: has type, path, format
/// </summary>
public class FabComponent
{
	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "type" )]
	public string Type { get; set; }

	[JsonPropertyName( "format" )]
	public string Format { get; set; }
}

/// <summary>
/// Represents a native file reference
/// </summary>
public class FabNativeFile
{
	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	[JsonPropertyName( "type" )]
	public string Type { get; set; }
}

/// <summary>
/// Represents LOD information
/// </summary>
public class FabLod
{
	[JsonPropertyName( "path" )]
	public string Path { get; set; }

	[JsonPropertyName( "file" )]
	public string File { get; set; }

	[JsonPropertyName( "lod" )]
	public int Lod { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	public string GetFilePath() => !string.IsNullOrEmpty( File ) ? File : Path;
}

/// <summary>
/// Represents metadata about the asset (legacy format)
/// </summary>
public class FabMeta
{
	[JsonPropertyName( "key" )]
	public string Key { get; set; }

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	// Value can be string, number, or boolean in the JSON
	[JsonPropertyName( "value" )]
	public object Value { get; set; }

	/// <summary>
	/// Gets the value as a string regardless of original type
	/// </summary>
	public string GetValueAsString() => Value?.ToString();
}

/// <summary>
/// Root metadata object containing launcher, megascans, and fab sections
/// </summary>
public class FabMetadata
{
	[JsonPropertyName( "launcher" )]
	public FabLauncherMetadata Launcher { get; set; }

	[JsonPropertyName( "megascans" )]
	public FabMegascansMetadata Megascans { get; set; }

	[JsonPropertyName( "fab" )]
	public FabFabMetadata Fab { get; set; }
}

/// <summary>
/// Launcher-specific metadata
/// </summary>
public class FabLauncherMetadata
{
	[JsonPropertyName( "version" )]
	public string Version { get; set; }

	[JsonPropertyName( "listening_port" )]
	public int ListeningPort { get; set; }
}

/// <summary>
/// Megascans-specific metadata (contains asset name, categories, maps, etc.)
/// </summary>
public class FabMegascansMetadata
{
	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "id" )]
	public string Id { get; set; }

	[JsonPropertyName( "categories" )]
	public List<string> Categories { get; set; } = new();

	[JsonPropertyName( "tags" )]
	public List<string> Tags { get; set; } = new();

	[JsonPropertyName( "physicalSize" )]
	public string PhysicalSize { get; set; }

	[JsonPropertyName( "highest_available_res" )]
	public int HighestAvailableRes { get; set; }

	[JsonPropertyName( "meta" )]
	public List<FabMeta> Meta { get; set; } = new();
}

/// <summary>
/// Fab marketplace metadata
/// </summary>
public class FabFabMetadata
{
	[JsonPropertyName( "listing" )]
	public FabListing Listing { get; set; }

	[JsonPropertyName( "target" )]
	public string Target { get; set; }

	[JsonPropertyName( "quality" )]
	public string Quality { get; set; }

	[JsonPropertyName( "format" )]
	public string Format { get; set; }

	[JsonPropertyName( "isQuixel" )]
	public bool IsQuixel { get; set; }
}

/// <summary>
/// Fab listing/product information
/// </summary>
public class FabListing
{
	[JsonPropertyName( "title" )]
	public string Title { get; set; }

	[JsonPropertyName( "uid" )]
	public string Uid { get; set; }

	[JsonPropertyName( "description" )]
	public string Description { get; set; }

	[JsonPropertyName( "thumbnail" )]
	public string Thumbnail { get; set; }

	[JsonPropertyName( "listingType" )]
	public string ListingType { get; set; }
}
