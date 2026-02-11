using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldLabs.API;

/// <summary>
/// Example MonoBehaviour demonstrating how to use the WorldLabs API client.
/// </summary>
public class WorldLabsExample : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Optional: Override API key (otherwise loaded from .env file)")]
    [SerializeField] private string apiKeyOverride;

    [Header("Generation Settings")]
    [SerializeField] private string textPrompt = "A mystical forest with glowing mushrooms and ancient trees";
    [SerializeField] private string displayName = "My Generated World";
    [SerializeField] private MarbleModel model = MarbleModel.Plus;

    [Header("Output")]
    [SerializeField] private RawImage thumbnailDisplay;
    [SerializeField] private RawImage panoramaDisplay;

    private WorldLabsClient _client;

    private void Start()
    {
        // Initialize the client
        // If apiKeyOverride is empty, it will load from .env file automatically
        _client = string.IsNullOrEmpty(apiKeyOverride)
            ? new WorldLabsClient()
            : new WorldLabsClient(apiKeyOverride);

        if (!_client.IsConfigured)
        {
            Debug.LogError("WorldLabs API key not configured! Set WORLDLABS_API_KEY in .env file.");
        }
    }

    #region Async Examples

    /// <summary>
    /// Example: Generate a world from text using async/await.
    /// </summary>
    public async void GenerateWorldFromTextAsync()
    {
        if (!_client.IsConfigured)
        {
            Debug.LogError("API key not configured!");
            return;
        }

        Debug.Log($"Starting world generation: {textPrompt}");

        try
        {
            // Start generation
            var generateResponse = await _client.GenerateWorldFromTextAsync(
                textPrompt: textPrompt,
                displayName: displayName,
                model: model
            );

            Debug.Log($"Generation started. Operation ID: {generateResponse.operation_id}");

            // Wait for completion with progress updates
            var result = await _client.WaitForOperationAsync(
                generateResponse.operation_id,
                onProgress: op =>
                {
                    if (op.metadata?.progress != null)
                    {
                        Debug.Log($"Progress: {op.metadata.progress:P0}");
                    }
                }
            );

            if (result.error != null)
            {
                Debug.LogError($"Generation failed: {result.error.message}");
                return;
            }

            Debug.Log($"World generated successfully!");
            Debug.Log($"  World ID: {result.response.world_id}");
            Debug.Log($"  Display Name: {result.response.display_name}");
            Debug.Log($"  Marble URL: {result.response.world_marble_url}");

            if (result.response.assets != null)
            {
                Debug.Log($"  Caption: {result.response.assets.caption}");
                Debug.Log($"  Thumbnail: {result.response.assets.thumbnail_url}");
                Debug.Log($"  Panorama: {result.response.assets.imagery?.pano_url}");
            }

            // Download thumbnail if display is assigned
            if (thumbnailDisplay != null && result.response.assets?.thumbnail_url != null)
            {
                var thumbnail = await _client.DownloadThumbnailAsync(result.response);
                thumbnailDisplay.texture = thumbnail;
            }
        }
        catch (WorldLabsException ex)
        {
            Debug.LogError($"WorldLabs API error: {ex.Message} (Status: {ex.StatusCode})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Generate world from an image URL.
    /// </summary>
    public async void GenerateWorldFromImageAsync()
    {
        if (!_client.IsConfigured) return;

        try
        {
            string imageUrl = "https://example.com/landscape.jpg";

            var world = await _client.GenerateFromImageAndWaitAsync(
                imageUrl: imageUrl,
                textPrompt: "A beautiful mountain landscape",
                displayName: "Image World",
                onProgress: progress => Debug.Log($"Progress: {progress:P0}")
            );

            Debug.Log($"World generated: {world.world_id}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: List all your worlds.
    /// </summary>
    public async void ListMyWorldsAsync()
    {
        if (!_client.IsConfigured) return;

        try
        {
            var response = await _client.ListWorldsAsync(
                pageSize: 10,
                status: WorldStatus.SUCCEEDED,
                sortBy: SortBy.created_at
            );

            Debug.Log($"Found {response.worlds.Count} worlds:");
            foreach (var world in response.worlds)
            {
                Debug.Log($"  - {world.display_name} ({world.world_id})");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Get a specific world by ID.
    /// </summary>
    public async void GetWorldByIdAsync(string worldId)
    {
        if (!_client.IsConfigured) return;

        try
        {
            var world = await _client.GetWorldAsync(worldId);
            Debug.Log($"World: {world.display_name}");
            Debug.Log($"  Created: {world.created_at}");
            Debug.Log($"  Model: {world.model}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Example: Upload a texture and use it for world generation.
    /// </summary>
    public async void GenerateFromTextureAsync(Texture2D texture)
    {
        if (!_client.IsConfigured) return;

        try
        {
            // Upload the texture first
            string mediaAssetId = await _client.UploadTextureAsync(texture, "myimage.png");
            Debug.Log($"Uploaded texture. Media Asset ID: {mediaAssetId}");

            // Generate world using the uploaded image
            var request = new WorldsGenerateRequest
            {
                world_prompt = ImagePrompt.FromMediaAsset(mediaAssetId),
                display_name = "World from Texture"
            };

            var generateResponse = await _client.GenerateWorldAsync(request);
            Debug.Log($"Generation started: {generateResponse.operation_id}");

            // Wait for completion...
            var result = await _client.WaitForOperationAsync(generateResponse.operation_id);
            Debug.Log($"World generated: {result.response?.world_id}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    #endregion

    #region Coroutine Examples

    /// <summary>
    /// Example: Generate a world using coroutines (for older Unity patterns).
    /// </summary>
    public void GenerateWorldCoroutine()
    {
        StartCoroutine(GenerateWorldCoroutineInternal());
    }

    private IEnumerator GenerateWorldCoroutineInternal()
    {
        if (!_client.IsConfigured)
        {
            Debug.LogError("API key not configured!");
            yield break;
        }

        Debug.Log($"Starting world generation: {textPrompt}");

        var request = new WorldsGenerateRequest
        {
            world_prompt = TextPrompt.Create(textPrompt),
            display_name = displayName,
            model = model == MarbleModel.Plus ? "Marble 0.1-plus" : "Marble 0.1-mini"
        };

        GenerateWorldResponse generateResponse = null;

        yield return _client.GenerateWorld(request, response => generateResponse = response);

        if (generateResponse == null)
        {
            Debug.LogError("Failed to start generation");
            yield break;
        }

        Debug.Log($"Generation started. Operation ID: {generateResponse.operation_id}");

        // Wait for completion
        GetOperationResponse result = null;
        bool completed = false;
        System.Exception error = null;

        yield return _client.WaitForOperation(
            generateResponse.operation_id,
            onComplete: r => { result = r; completed = true; },
            onProgress: r => Debug.Log($"Progress update received"),
            onError: ex => { error = ex; completed = true; }
        );

        if (error != null)
        {
            Debug.LogError($"Generation failed: {error.Message}");
            yield break;
        }

        if (result?.response != null)
        {
            Debug.Log($"World generated: {result.response.world_id}");
            Debug.Log($"  Display Name: {result.response.display_name}");
        }
    }

    #endregion

    #region UI Callbacks

    /// <summary>
    /// Call this from a UI button to trigger generation.
    /// </summary>
    public void OnGenerateButtonClicked()
    {
        GenerateWorldFromTextAsync();
    }

    /// <summary>
    /// Call this from a UI button to list worlds.
    /// </summary>
    public void OnListWorldsButtonClicked()
    {
        ListMyWorldsAsync();
    }

    #endregion
}
