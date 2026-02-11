using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldLabs.API
{
    #region Enums

    /// <summary>
    /// High-level media asset type.
    /// </summary>
    [Serializable]
    public enum MediaAssetKind
    {
        Image,
        Video
    }

    /// <summary>
    /// Model options for world generation.
    /// </summary>
    [Serializable]
    public enum MarbleModel
    {
        [InspectorName("Marble 0.1-mini")]
        Mini,
        [InspectorName("Marble 0.1-plus")]
        Plus
    }

    /// <summary>
    /// World status filter options.
    /// </summary>
    [Serializable]
    public enum WorldStatus
    {
        SUCCEEDED,
        PENDING,
        FAILED,
        RUNNING
    }

    /// <summary>
    /// Sort options for listing worlds.
    /// </summary>
    [Serializable]
    public enum SortBy
    {
        created_at,
        updated_at
    }

    #endregion

    #region Content References

    /// <summary>
    /// Represents content that can be stored inline or via URL.
    /// Supports both direct data storage (up to 10MB) and URL references (up to 20MB).
    /// </summary>
    [Serializable]
    public class Content
    {
        public string data_base64;
        public string extension;
        public string uri;
    }

    /// <summary>
    /// Reference to content via base64-encoded data.
    /// </summary>
    [Serializable]
    public class DataBase64Reference
    {
        public string source = "data_base64";
        public string data_base64;
        public string extension;

        public static DataBase64Reference FromBytes(byte[] data, string extension)
        {
            return new DataBase64Reference
            {
                data_base64 = Convert.ToBase64String(data),
                extension = extension
            };
        }

        public static DataBase64Reference FromTexture(Texture2D texture, string extension = "png")
        {
            byte[] data = extension.ToLower() == "jpg" || extension.ToLower() == "jpeg"
                ? texture.EncodeToJPG()
                : texture.EncodeToPNG();
            return FromBytes(data, extension);
        }
    }

    /// <summary>
    /// Reference to content via a publicly accessible URL.
    /// </summary>
    [Serializable]
    public class UriReference
    {
        public string source = "uri";
        public string uri;

        public static UriReference FromUrl(string url)
        {
            return new UriReference { uri = url };
        }
    }

    /// <summary>
    /// Reference to a previously uploaded MediaAsset.
    /// </summary>
    [Serializable]
    public class MediaAssetReference
    {
        public string source = "media_asset";
        public string media_asset_id;

        public static MediaAssetReference FromId(string mediaAssetId)
        {
            return new MediaAssetReference { media_asset_id = mediaAssetId };
        }
    }

    #endregion

    #region Prompts

    /// <summary>
    /// Base class for world prompts.
    /// </summary>
    [Serializable]
    public abstract class WorldPrompt
    {
        public abstract string type { get; }
    }

    /// <summary>
    /// Text-to-world generation prompt.
    /// </summary>
    [Serializable]
    public class TextPrompt : WorldPrompt
    {
        public override string type => "text";
        public string text_prompt;
        public bool? disable_recaption;

        public static TextPrompt Create(string textPrompt, bool? disableRecaption = null)
        {
            return new TextPrompt
            {
                text_prompt = textPrompt,
                disable_recaption = disableRecaption
            };
        }
    }

    /// <summary>
    /// Image-to-world generation prompt.
    /// Recommended image formats: jpg, jpeg, png, webp.
    /// </summary>
    [Serializable]
    public class ImagePrompt : WorldPrompt
    {
        public override string type => "image";
        public object image_prompt; // Can be DataBase64Reference, UriReference, or MediaAssetReference
        public string text_prompt;
        public bool? is_pano;
        public bool? disable_recaption;

        public static ImagePrompt FromUrl(string imageUrl, string textPrompt = null, bool? isPano = null)
        {
            return new ImagePrompt
            {
                image_prompt = UriReference.FromUrl(imageUrl),
                text_prompt = textPrompt,
                is_pano = isPano
            };
        }

        public static ImagePrompt FromBase64(byte[] imageData, string extension, string textPrompt = null, bool? isPano = null)
        {
            return new ImagePrompt
            {
                image_prompt = DataBase64Reference.FromBytes(imageData, extension),
                text_prompt = textPrompt,
                is_pano = isPano
            };
        }

        public static ImagePrompt FromTexture(Texture2D texture, string extension = "png", string textPrompt = null, bool? isPano = null)
        {
            return new ImagePrompt
            {
                image_prompt = DataBase64Reference.FromTexture(texture, extension),
                text_prompt = textPrompt,
                is_pano = isPano
            };
        }

        public static ImagePrompt FromMediaAsset(string mediaAssetId, string textPrompt = null, bool? isPano = null)
        {
            return new ImagePrompt
            {
                image_prompt = MediaAssetReference.FromId(mediaAssetId),
                text_prompt = textPrompt,
                is_pano = isPano
            };
        }
    }

    /// <summary>
    /// Content with a preferred location on the sphere.
    /// </summary>
    [Serializable]
    public class SphericallyLocatedContent
    {
        public object content; // Can be DataBase64Reference, UriReference, or MediaAssetReference
        public float? azimuth; // Azimuth angle in degrees

        public static SphericallyLocatedContent FromUrl(string url, float? azimuth = null)
        {
            return new SphericallyLocatedContent
            {
                content = UriReference.FromUrl(url),
                azimuth = azimuth
            };
        }

        public static SphericallyLocatedContent FromBase64(byte[] data, string extension, float? azimuth = null)
        {
            return new SphericallyLocatedContent
            {
                content = DataBase64Reference.FromBytes(data, extension),
                azimuth = azimuth
            };
        }

        public static SphericallyLocatedContent FromMediaAsset(string mediaAssetId, float? azimuth = null)
        {
            return new SphericallyLocatedContent
            {
                content = MediaAssetReference.FromId(mediaAssetId),
                azimuth = azimuth
            };
        }
    }

    /// <summary>
    /// Multi-image-to-world generation prompt.
    /// Recommended image formats: jpg, jpeg, png, webp.
    /// </summary>
    [Serializable]
    public class MultiImagePrompt : WorldPrompt
    {
        public override string type => "multi-image";
        public List<SphericallyLocatedContent> multi_image_prompt = new List<SphericallyLocatedContent>();
        public string text_prompt;
        public bool reconstruct_images = false;
        public bool? disable_recaption;

        public void AddImage(SphericallyLocatedContent image)
        {
            multi_image_prompt.Add(image);
        }

        public void AddImageFromUrl(string url, float? azimuth = null)
        {
            multi_image_prompt.Add(SphericallyLocatedContent.FromUrl(url, azimuth));
        }
    }

    /// <summary>
    /// Video-to-world generation prompt.
    /// Recommended video formats: mp4, webm, mov, avi.
    /// Maximum video size: 100MB.
    /// </summary>
    [Serializable]
    public class VideoPrompt : WorldPrompt
    {
        public override string type => "video";
        public object video_prompt; // Can be DataBase64Reference, UriReference, or MediaAssetReference
        public string text_prompt;
        public bool? disable_recaption;

        public static VideoPrompt FromUrl(string videoUrl, string textPrompt = null)
        {
            return new VideoPrompt
            {
                video_prompt = UriReference.FromUrl(videoUrl),
                text_prompt = textPrompt
            };
        }

        public static VideoPrompt FromMediaAsset(string mediaAssetId, string textPrompt = null)
        {
            return new VideoPrompt
            {
                video_prompt = MediaAssetReference.FromId(mediaAssetId),
                text_prompt = textPrompt
            };
        }
    }

    /// <summary>
    /// Depth pano prompt for models conditioned on a depth pano and text.
    /// </summary>
    [Serializable]
    public class DepthPanoPrompt : WorldPrompt
    {
        public override string type => "depth-pano";
        public Content depth_pano_image;
        public string text_prompt;
        public float z_min;
        public float z_max;
    }

    /// <summary>
    /// Inpaint pano prompt for models that inpaint the masked portion of a pano image.
    /// </summary>
    [Serializable]
    public class InpaintPanoPrompt : WorldPrompt
    {
        public override string type => "inpaint-pano";
        public Content pano_image;
        public Content pano_mask;
        public string text_prompt;
    }

    #endregion

    #region Permissions

    /// <summary>
    /// Access control permissions for a resource.
    /// </summary>
    [Serializable]
    public class Permission
    {
        public List<string> allowed_readers = new List<string>();
        public List<string> allowed_writers = new List<string>();
        public bool @public = false;

        public static Permission Private => new Permission { @public = false };
        public static Permission Public => new Permission { @public = true };
    }

    #endregion

    #region Assets

    /// <summary>
    /// Imagery asset URLs.
    /// </summary>
    [Serializable]
    public class ImageryAssets
    {
        public string pano_url;
    }

    /// <summary>
    /// Mesh asset URLs.
    /// </summary>
    [Serializable]
    public class MeshAssets
    {
        public string collider_mesh_url;
    }

    /// <summary>
    /// Gaussian splat asset URLs.
    /// Keys are resolution names like "100k", "500k", "full_res"
    /// </summary>
    [Serializable]
    public class SplatAssets
    {
        // Note: Unity's JsonUtility doesn't support Dictionary, so we store the raw JSON
        // and parse it manually. The spz_urls field will be populated by custom deserialization.
        [NonSerialized]
        public Dictionary<string, string> spz_urls;

        // Raw JSON string for manual parsing (set by custom deserializer)
        [NonSerialized]
        public string spz_urls_raw;

        /// <summary>
        /// Gets available resolution options (e.g., "100k", "500k", "full_res")
        /// </summary>
        public List<string> GetAvailableResolutions()
        {
            if (spz_urls == null) return new List<string>();
            return new List<string>(spz_urls.Keys);
        }

        /// <summary>
        /// Gets the URL for a specific resolution
        /// </summary>
        public string GetUrl(string resolution)
        {
            if (spz_urls != null && spz_urls.TryGetValue(resolution, out string url))
            {
                return url;
            }
            return null;
        }

        /// <summary>
        /// Gets the best available resolution URL (prefers full_res > 500k > 100k)
        /// </summary>
        public string GetBestResolutionUrl()
        {
            if (spz_urls == null) return null;
            
            string[] preferredOrder = { "full_res", "500k", "100k" };
            foreach (var res in preferredOrder)
            {
                if (spz_urls.TryGetValue(res, out string url))
                {
                    return url;
                }
            }

            // Return first available if none of the preferred ones exist
            foreach (var kvp in spz_urls)
            {
                return kvp.Value;
            }

            return null;
        }
    }

    /// <summary>
    /// Downloadable outputs of world generation.
    /// </summary>
    [Serializable]
    public class WorldAssets
    {
        public string caption;
        public ImageryAssets imagery;
        public MeshAssets mesh;
        public SplatAssets splats;
        public string thumbnail_url;
    }

    #endregion

    #region World

    /// <summary>
    /// A generated world, including asset URLs.
    /// </summary>
    [Serializable]
    public class World
    {
        public string world_id;
        public string display_name;
        public string world_marble_url;
        public WorldAssets assets;
        public string created_at;
        public string updated_at;
        public Permission permission;
        public string model;
        public List<string> tags;
    }

    #endregion

    #region Operations

    /// <summary>
    /// Error information for a failed operation.
    /// </summary>
    [Serializable]
    public class OperationError
    {
        public int? code;
        public string message;
    }

    /// <summary>
    /// Response from world generation endpoint.
    /// </summary>
    [Serializable]
    public class GenerateWorldResponse
    {
        public string operation_id;
        public bool done;
        public string created_at;
        public string updated_at;
        public string expires_at;
        public OperationError error;
        public object metadata;
        public object response;
    }

    /// <summary>
    /// Response from get operation endpoint.
    /// </summary>
    [Serializable]
    public class GetOperationResponse
    {
        public string operation_id;
        public bool done;
        public string created_at;
        public string updated_at;
        public string expires_at;
        public OperationError error;
        public OperationMetadata metadata;
        public World response;
    }

    /// <summary>
    /// Metadata for operation progress.
    /// </summary>
    [Serializable]
    public class OperationMetadata
    {
        public string world_id;
        public float? progress;
    }

    #endregion

    #region Media Assets

    /// <summary>
    /// A user-uploaded media asset stored in managed storage.
    /// </summary>
    [Serializable]
    public class MediaAsset
    {
        public string media_asset_id;
        public string file_name;
        public string extension;
        public string kind;
        public string created_at;
        public string updated_at;
        public object metadata;
    }

    /// <summary>
    /// Information required to upload raw bytes directly to storage.
    /// </summary>
    [Serializable]
    public class UploadUrlInfo
    {
        public string upload_url;
        public string upload_method;
        public Dictionary<string, string> required_headers;
        public string curl_example;
    }

    /// <summary>
    /// Response from preparing a media asset upload.
    /// </summary>
    [Serializable]
    public class MediaAssetPrepareUploadResponse
    {
        public MediaAsset media_asset;
        public UploadUrlInfo upload_info;
    }

    #endregion

    #region Requests

    /// <summary>
    /// Request to generate a world.
    /// </summary>
    [Serializable]
    public class WorldsGenerateRequest
    {
        public object world_prompt; // WorldPrompt subclass
        public string display_name;
        public string model = "Marble 0.1-plus";
        public int? seed;
        public List<string> tags;
        public Permission permission;
    }

    /// <summary>
    /// Request to prepare a media asset upload.
    /// </summary>
    [Serializable]
    public class MediaAssetPrepareUploadRequest
    {
        public string file_name;
        public string kind;
        public string extension;
        public object metadata;
    }

    /// <summary>
    /// Request to list worlds with optional filters.
    /// </summary>
    [Serializable]
    public class ListWorldsRequest
    {
        public int page_size = 20;
        public string page_token;
        public string status;
        public string model;
        public List<string> tags;
        public bool? is_public;
        public string created_after;
        public string created_before;
        public string sort_by = "created_at";
        public string nonce; // Unique ID to prevent caching (renamed from _cache_buster - underscore prefix may not serialize)
    }

    /// <summary>
    /// Response containing a list of worlds.
    /// </summary>
    [Serializable]
    public class ListWorldsResponse
    {
        public List<World> worlds;
        public string next_page_token;
    }

    #endregion

    #region Validation Errors

    /// <summary>
    /// HTTP Validation Error.
    /// </summary>
    [Serializable]
    public class HTTPValidationError
    {
        public List<ValidationErrorDetail> detail;
    }

    /// <summary>
    /// Validation error detail.
    /// </summary>
    [Serializable]
    public class ValidationErrorDetail
    {
        public List<object> loc;
        public string msg;
        public string type;
    }

    #endregion
}
