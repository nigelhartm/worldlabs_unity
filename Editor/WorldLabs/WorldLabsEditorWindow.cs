using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GaussianSplatting.Editor;
using GaussianSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
#if GS_ENABLE_URP
using UnityEngine.Rendering.Universal;
#endif
using WorldLabs.API;

namespace WorldLabs.Editor
{
    /// <summary>
    /// Compression quality levels for Gaussian Splat assets
    /// </summary>
    public enum CompressionQuality
    {
        VeryHigh,
        High,
        Medium,
        Low,
        VeryLow
    }

    /// <summary>
    /// Main editor window for WorldLabs Unity Integration.
    /// Provides UI for viewing, generating, and importing worlds.
    /// </summary>
    public class WorldLabsEditorWindow : EditorWindow
    {
        #region Constants

        private const string WINDOW_TITLE = "WorldLabs Unity Integration";
        private const string DEFAULT_WORLDS_FOLDER = "Assets/WorldLabsWorlds";
        private const string WORLDS_FOLDER_PREF_KEY = "WorldLabs.OutputFolder";
        private const int THUMBNAIL_SIZE = 128;
        private const int GRID_COLUMNS = 3;
        private const float POLL_INTERVAL = 5f;
        private const bool DEBUG_API_CALLS = false;
        private const int PAGE_SIZE = 11;

        #endregion

        #region Enums

        private enum ViewState
        {
            WorldsList,
            CreateWorld,
            Settings
        }

        private enum PromptType
        {
            Text,
            ImageUrl,
            ImageFile,
            VideoUrl
        }

        private enum ImportFormat
        {
            Mesh,
            GaussianSplat
        }

        private enum ModelFilter
        {
            All,
            Plus,
            Mini
        }

        private enum VisibilityFilter
        {
            All,
            Public,
            Private
        }

        #endregion

        #region Private Fields

        // Client
        private WorldLabsClient _client;
        private bool _isInitialized;
        private string _initError;

        private string _worldsFolder;

        // View State
        private ViewState _currentView = ViewState.WorldsList;
        private Vector2 _scrollPosition;

        // Worlds List
        private List<WorldItem> _worlds = new List<WorldItem>();
        private bool _isLoadingWorlds;
        private bool _pendingRefresh;
        private string _createdAfterFilter;
        private string _createdBeforeFilter;
        private string _loadError;
        private float _lastPollTime;
        private SortBy _sortBy = SortBy.created_at;
        private ModelFilter _modelFilter = ModelFilter.All;
        private VisibilityFilter _visibilityFilter = VisibilityFilter.All;

        // Pagination
        private string _currentPageToken = null;
        private string _nextPageToken = null;
        private List<string> _pageTokenHistory = new List<string>();
        private int _currentPageIndex = 0;

        // Create World
        private PromptType _promptType = PromptType.Text;
        private string _textPrompt = "";
        private string _imageUrl = "";
        private string _videoUrl = "";
        private Texture2D _selectedImage;
        private string _displayName = "";
        private MarbleModel _model = MarbleModel.Plus;
        private bool _isPublic = false;
        private List<string> _tags = new List<string>();
        private string _newTag = "";
        private bool _isGenerating;
        private string _generateError;

        // Pending Operations
        private Dictionary<string, PendingOperation> _pendingOperations = new Dictionary<string, PendingOperation>();

        // Import Settings Selection
        public static CompressionQuality _selectedCompressionQuality = CompressionQuality.Medium;
        public static bool _loadToScene = true;
        public static ImportSettingsDialog _importSettingsDialog;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _worldCardStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _centerLabelStyle;
        private GUIStyle _textAreaStyle;
        private bool _stylesInitialized;

        #endregion

        #region Data Classes

        private class WorldItem
        {
            public World World;
            public Texture2D Thumbnail;
            public bool IsLoadingThumbnail;
            public bool IsImporting;
            public float ImportProgress;
        }

        private class PendingOperation
        {
            public string OperationId;
            public string DisplayName;
            public float StartTime;
            public float Progress;
            public string Status;
            public bool IsComplete;
            public bool HasError;
            public string ErrorMessage;
            public string PreviewUrl; // URL to world marble preview
            public bool CanShowPreview => !string.IsNullOrEmpty(PreviewUrl);
        }

        #endregion

        #region Menu Item

        [MenuItem("WorldLabs/WorldLabsUnityIntegration")]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldLabsEditorWindow>();
            window.titleContent = new GUIContent(WINDOW_TITLE);
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            Initialize();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnGUI()
        {
            InitializeStyles();

            if (!_isInitialized)
            {
                DrawInitializationError();
                return;
            }

            DrawHeader();

            switch (_currentView)
            {
                case ViewState.WorldsList:
                    DrawWorldsList();
                    break;
                case ViewState.CreateWorld:
                    DrawCreateWorld();
                    break;
                case ViewState.Settings:
                    DrawSettings();
                    break;
            }
        }

        private void OnEditorUpdate()
        {
            // Poll for pending operations
            if (_pendingOperations.Count > 0 && Time.realtimeSinceStartup - _lastPollTime > POLL_INTERVAL)
            {
                _lastPollTime = Time.realtimeSinceStartup;
                PollPendingOperations();
            }
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            try
            {
                _client = new WorldLabsClient();
                _worldsFolder = LoadWorldsFolder();

                if (!_client.IsConfigured)
                {
                    _initError = "API key not configured. Please set WORLDLABS_API_KEY in the .env file in your project root.";
                    _isInitialized = false;
                    return;
                }

                _isInitialized = true;
                RefreshWorldsList();
            }
            catch (Exception ex)
            {
                _initError = $"Failed to initialize: {ex.Message}";
                _isInitialized = false;
            }
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 5, 5)
            };

            _worldCardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(5, 5, 5, 5)
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(15, 15, 8, 8)
            };

            _centerLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };

            _textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true
            };

            _stylesInitialized = true;
        }

        #endregion

        #region Header

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Toggle(_currentView == ViewState.WorldsList, "My Worlds", EditorStyles.toolbarButton))
            {
                if (_currentView != ViewState.WorldsList)
                {
                    _currentView = ViewState.WorldsList;
                    RefreshWorldsList();
                }
            }

            if (GUILayout.Toggle(_currentView == ViewState.CreateWorld, "Create World", EditorStyles.toolbarButton))
            {
                _currentView = ViewState.CreateWorld;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("⚙ Refresh", EditorStyles.toolbarButton))
            {
                SetRefreshTimeWindow();
                ResetPagination();
                RefreshWorldsList();
            }

            if (GUILayout.Button("⚙ Settings", EditorStyles.toolbarButton))
            {
                _currentView = ViewState.Settings;
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Worlds List View

        private void DrawWorldsList()
        {
            if (_isLoadingWorlds)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("Loading worlds...", _centerLabelStyle);
                return;
            }

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
                if (GUILayout.Button("Retry"))
                {
                    SetRefreshTimeWindow();
                    RefreshWorldsList();
                }
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Draw pending operations first
            if (_pendingOperations.Count > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("In Progress", _subHeaderStyle);
                DrawPendingOperations();
                EditorGUILayout.Space(10);
                DrawSeparator();
            }

            // Draw "Create New" card and existing worlds in a grid
            EditorGUILayout.Space(10);
            
            // Header with filter and sort controls
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Your Worlds", _subHeaderStyle);
            GUILayout.FlexibleSpace();
            
            // TODO: Visibility filter - API support pending
            // EditorGUILayout.LabelField("Visibility:", GUILayout.Width(60));
            // var newVisibilityFilter = (VisibilityFilter)EditorGUILayout.EnumPopup(_visibilityFilter, GUILayout.Width(80));
            // if (newVisibilityFilter != _visibilityFilter)
            // {
            //     _visibilityFilter = newVisibilityFilter;
            //     SetRefreshTimeWindow();
            //     ResetPagination();
            //     RefreshWorldsList();
            // }
            
            EditorGUILayout.LabelField("Model:", GUILayout.Width(45));
            var newModelFilter = (ModelFilter)EditorGUILayout.EnumPopup(_modelFilter, GUILayout.Width(70));
            if (newModelFilter != _modelFilter)
            {
                _modelFilter = newModelFilter;
                SetRefreshTimeWindow();
                ResetPagination();
                RefreshWorldsList();
            }
            
            EditorGUILayout.LabelField("Sort by:", GUILayout.Width(50));
            var newSortBy = (SortBy)EditorGUILayout.EnumPopup(_sortBy, GUILayout.Width(100));
            if (newSortBy != _sortBy)
            {
                _sortBy = newSortBy;
                SetRefreshTimeWindow();
                ResetPagination();
                RefreshWorldsList();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);

            DrawWorldsGrid();

            EditorGUILayout.Space(10);
            DrawPaginationControls();
            EditorGUILayout.Space(10);

            EditorGUILayout.EndScrollView();
        }

        private void DrawPendingOperations()
        {
            foreach (var kvp in _pendingOperations)
            {
                var op = kvp.Value;
                EditorGUILayout.BeginVertical(_worldCardStyle);

                EditorGUILayout.LabelField(op.DisplayName ?? "Generating...", EditorStyles.boldLabel);

                if (op.HasError)
                {
                    EditorGUILayout.HelpBox(op.ErrorMessage, MessageType.Error);
                }
                else
                {
                    // Show status without percentage
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(20)), op.Progress, op.Status ?? "Processing...");
                    
                    // Show preview button if available
                    if (op.CanShowPreview)
                    {
                        if (GUILayout.Button("Watch Preview", EditorStyles.miniButton))
                        {
                            Application.OpenURL(op.PreviewUrl);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawWorldsGrid()
        {
            float windowWidth = position.width - 40;
            float cardWidth = (windowWidth / GRID_COLUMNS) - 10;

            int itemsPerRow = GRID_COLUMNS;
            int totalItems = _worlds.Count + 1; // +1 for create button
            int rows = Mathf.CeilToInt((float)totalItems / itemsPerRow);

            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < itemsPerRow; col++)
                {
                    int index = row * itemsPerRow + col;

                    if (index == 0)
                    {
                        // Create New World card
                        DrawCreateNewCard(cardWidth);
                    }
                    else if (index - 1 < _worlds.Count)
                    {
                        DrawWorldCard(_worlds[index - 1], cardWidth);
                    }
                    else
                    {
                        GUILayout.Space(cardWidth);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawCreateNewCard(float width)
        {
            EditorGUILayout.BeginVertical(_worldCardStyle, GUILayout.Width(width), GUILayout.Height(THUMBNAIL_SIZE + 120));

            // Empty thumbnail area with plus icon
            Rect thumbnailRect = GUILayoutUtility.GetRect(width - 20, THUMBNAIL_SIZE);
            EditorGUI.DrawRect(thumbnailRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));

            var plusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(thumbnailRect, "+", plusStyle);

            EditorGUILayout.Space(5);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Create New World", EditorStyles.miniButton))
            {
                _currentView = ViewState.CreateWorld;
                ResetCreateWorldForm();
            }

            if (GUILayout.Button("Import from File", EditorStyles.miniButton))
            {
                ImportFromFile();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWorldCard(WorldItem item, float width)
        {
            EditorGUILayout.BeginVertical(_worldCardStyle, GUILayout.Width(width), GUILayout.Height(THUMBNAIL_SIZE + 120));

            // Thumbnail
            Rect thumbnailRect = GUILayoutUtility.GetRect(width - 20, THUMBNAIL_SIZE);

            if (item.Thumbnail != null)
            {
                GUI.DrawTexture(thumbnailRect, item.Thumbnail, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.DrawRect(thumbnailRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
                GUI.Label(thumbnailRect, item.IsLoadingThumbnail ? "Loading..." : "No Preview", _centerLabelStyle);

                if (!item.IsLoadingThumbnail && item.World.assets?.thumbnail_url != null)
                {
                    LoadThumbnail(item);
                }
            }

            EditorGUILayout.Space(5);

            // Title
            EditorGUILayout.LabelField(item.World.display_name ?? "Untitled", EditorStyles.boldLabel);

            // Model
            if (!string.IsNullOrEmpty(item.World.model))
            {
                EditorGUILayout.LabelField($"Model: {item.World.model}", EditorStyles.miniLabel);
            }

            // Created At
            if (!string.IsNullOrEmpty(item.World.created_at))
            {
                string formattedDate = FormatDateTime(item.World.created_at);
                EditorGUILayout.LabelField($"Created: {formattedDate}", EditorStyles.miniLabel);
            }

            // Tags
            if (item.World.tags != null && item.World.tags.Count > 0)
            {
                string tagsText = string.Join(", ", item.World.tags);
                EditorGUILayout.LabelField($"Tags: {tagsText}", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();

            // Import button for successful worlds
            if (item.World.assets != null)
            {
                EditorGUILayout.BeginHorizontal();

                if (item.IsImporting)
                {
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(20)), item.ImportProgress, "Importing...");
                }
                else
                {
                    if (GUILayout.Button("Import", EditorStyles.miniButton))
                    {
                        ShowImportMenu(item);
                    }

                    if (GUILayout.Button("View", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        Application.OpenURL(item.World.world_marble_url);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void ShowImportMenu(WorldItem item)
        {
            var menu = new GenericMenu();

            // Mesh import option
            bool isMiniModel = string.Equals(item.World.model, "Marble 0.1-mini", StringComparison.OrdinalIgnoreCase);
            if (isMiniModel)
            {
                menu.AddDisabledItem(new GUIContent("Mesh/Unavailable for Marble 0.1-mini"));
            }
            else if (item.World.assets?.mesh?.collider_mesh_url != null)
            {
                menu.AddItem(new GUIContent("Mesh/Import Collider Mesh"), false, () => ImportWorld(item, ImportFormat.Mesh));
            }
            else
            {
                // Display friendlier message when collider mesh is not yet produced
                menu.AddDisabledItem(new GUIContent("Mesh/Not Available Yet"));
            }

            menu.AddSeparator("");

            // Gaussian Splat import options with resolution choices
            if (item.World.assets?.splats?.spz_urls != null && item.World.assets.splats.spz_urls.Count > 0)
            {
                var resolutions = item.World.assets.splats.GetAvailableResolutions();

                // Add options in preferred order
                string[] preferredOrder = { "full_res", "500k", "100k" };
                foreach (var res in preferredOrder)
                {
                    if (resolutions.Contains(res))
                    {
                        string displayName = GetResolutionDisplayName(res);
                        menu.AddItem(new GUIContent($"Gaussian Splat/{displayName}"), false, 
                            () => ImportGaussianSplatWithResolution(item, res));
                    }
                }

                // Add any other resolutions not in the preferred order
                foreach (var res in resolutions)
                {
                    if (!System.Array.Exists(preferredOrder, p => p == res))
                    {
                        menu.AddItem(new GUIContent($"Gaussian Splat/{res}"), false, 
                            () => ImportGaussianSplatWithResolution(item, res));
                    }
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Gaussian Splat/Not Available"));
            }

            menu.ShowAsContext();
        }

        private string GetResolutionDisplayName(string resolution)
        {
            switch (resolution)
            {
                case "full_res": return "Full Resolution";
                case "500k": return "500K Points";
                case "100k": return "100K Points";
                default: return resolution;
            }
        }

        private void ImportFromFile()
        {
            string filePath = EditorUtility.OpenFilePanel("Import Gaussian Splat", "", "spz,ply");
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            string extension = Path.GetExtension(filePath).ToLower();
            if (extension != ".spz" && extension != ".ply")
            {
                EditorUtility.DisplayDialog("Invalid File", "Please select a .spz or .ply file.", "OK");
                return;
            }

            try
            {
                EnsureWorldsFolderExists();

                // Create a folder based on the file name
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string safeName = SanitizeFileName(fileNameWithoutExt);
                string worldFolder = $"{GetWorldsFolder()}/{safeName}";
                worldFolder = worldFolder.Replace("\\", "/");

                if (!Directory.Exists(worldFolder))
                {
                    Directory.CreateDirectory(worldFolder);
                }

                EditorUtility.DisplayProgressBar("Importing", "Creating Gaussian Splat Asset...", 0.3f);

                // Copy file to the world folder if not already there
                string destPath = $"{worldFolder}/{Path.GetFileName(filePath)}";
                if (!filePath.Replace("\\", "/").Equals(destPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(filePath, destPath, true);
                }

                EditorUtility.DisplayProgressBar("Importing", "Processing...", 0.5f);

                // Ask for compression quality and load to scene preference via dialog
                ShowCompressionQualityDialog((quality, loadToScene) => 
                {
                    try
                    {
                        EditorUtility.DisplayProgressBar("Importing", "Creating Gaussian Splat Asset...", 0.5f);
                        
                        GetFormatSettingsForQuality(quality, out var formatPos, out var formatScale, out var formatColor, out var formatSH);
                        
                        // Create the asset
                        GaussianSplatAsset asset = GaussianSplatAssetCreatorAPI.CreateAsset(
                            destPath,
                            worldFolder,
                            safeName,
                            formatPos,
                            formatScale,
                            formatColor,
                            formatSH,
                            false
                        );

                        EditorUtility.ClearProgressBar();

                        if (asset == null)
                        {
                            EditorUtility.DisplayDialog("Import Failed", "Failed to create Gaussian Splat Asset from file.", "OK");
                            return;
                        }

                        AssetDatabase.Refresh();

                        if (loadToScene)
                        {
                            AddGaussianSplatToScene(asset, safeName);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Import Complete", $"Gaussian Splat imported to:\n{worldFolder}", "OK");
                            EditorUtility.RevealInFinder(worldFolder);
                        }

                        Debug.Log($"[WorldLabs] Imported Gaussian Splat from file: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("Import Failed", $"Error: {ex.Message}", "OK");
                        Debug.LogError($"[WorldLabs] Import from file failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Import Failed", $"Error: {ex.Message}", "OK");
                Debug.LogError($"[WorldLabs] Import from file failed: {ex}");
            }
        }

        #endregion

        #region Create World View

        private void DrawCreateWorld()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(10);

            // Prompt Type Selection
            EditorGUILayout.LabelField("Input Type", _subHeaderStyle);
            _promptType = (PromptType)EditorGUILayout.EnumPopup("Source", _promptType);

            EditorGUILayout.Space(10);

            // Prompt Input based on type
            EditorGUILayout.LabelField("Prompt", _subHeaderStyle);
            DrawPromptInput();

            EditorGUILayout.Space(10);
            DrawSeparator();
            EditorGUILayout.Space(10);

            // Metadata
            EditorGUILayout.LabelField("World Settings", _subHeaderStyle);
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            _model = (MarbleModel)EditorGUILayout.EnumPopup("Model", _model);

            EditorGUILayout.Space(5);

            // TODO: Tags - API support pending
            // EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
            // DrawTagsEditor();

            EditorGUILayout.Space(20);

            // Error display
            if (!string.IsNullOrEmpty(_generateError))
            {
                EditorGUILayout.HelpBox(_generateError, MessageType.Error);
                EditorGUILayout.Space(10);
            }

            // Generate button
            EditorGUI.BeginDisabledGroup(_isGenerating || !IsValidInput());
            if (GUILayout.Button(_isGenerating ? "Generating..." : "Generate World", GUILayout.Height(40)))
            {
                StartGeneration();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(20);

            EditorGUILayout.EndScrollView();
        }

        private void DrawPromptInput()
        {
            switch (_promptType)
            {
                case PromptType.Text:
                    EditorGUILayout.LabelField("Describe the world you want to create:");
                    _textPrompt = EditorGUILayout.TextArea(_textPrompt, _textAreaStyle, GUILayout.Height(80));
                    break;

                case PromptType.ImageUrl:
                    _imageUrl = EditorGUILayout.TextField("Image URL", _imageUrl);
                    EditorGUILayout.LabelField("Optional text guidance:");
                    _textPrompt = EditorGUILayout.TextArea(_textPrompt, _textAreaStyle, GUILayout.Height(60));
                    break;

                case PromptType.ImageFile:
                    EditorGUILayout.BeginHorizontal();
                    _selectedImage = (Texture2D)EditorGUILayout.ObjectField("Image", _selectedImage, typeof(Texture2D), false);
                    EditorGUILayout.EndHorizontal();

                    if (_selectedImage != null)
                    {
                        Rect previewRect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(false));
                        GUI.DrawTexture(previewRect, _selectedImage, ScaleMode.ScaleToFit);
                    }

                    EditorGUILayout.LabelField("Optional text guidance:");
                    _textPrompt = EditorGUILayout.TextArea(_textPrompt, _textAreaStyle, GUILayout.Height(60));
                    break;

                case PromptType.VideoUrl:
                    _videoUrl = EditorGUILayout.TextField("Video URL", _videoUrl);
                    EditorGUILayout.LabelField("Optional text guidance:");
                    _textPrompt = EditorGUILayout.TextArea(_textPrompt, _textAreaStyle, GUILayout.Height(60));
                    break;
            }
        }

        private void DrawTagsEditor()
        {
            // Display existing tags
            EditorGUILayout.BeginHorizontal();
            for (int i = _tags.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Width(100));
                EditorGUILayout.LabelField(_tags[i], GUILayout.Width(70));
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    _tags.RemoveAt(i);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndHorizontal();

            // Add new tag
            EditorGUILayout.BeginHorizontal();
            _newTag = EditorGUILayout.TextField(_newTag, GUILayout.Width(150));
            if (GUILayout.Button("Add Tag", GUILayout.Width(80)) && !string.IsNullOrWhiteSpace(_newTag))
            {
                if (!_tags.Contains(_newTag))
                {
                    _tags.Add(_newTag.Trim());
                }
                _newTag = "";
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool IsValidInput()
        {
            switch (_promptType)
            {
                case PromptType.Text:
                    return !string.IsNullOrWhiteSpace(_textPrompt);
                case PromptType.ImageUrl:
                    return !string.IsNullOrWhiteSpace(_imageUrl);
                case PromptType.ImageFile:
                    return _selectedImage != null;
                case PromptType.VideoUrl:
                    return !string.IsNullOrWhiteSpace(_videoUrl);
                default:
                    return false;
            }
        }

        private void ResetCreateWorldForm()
        {
            _textPrompt = "";
            _imageUrl = "";
            _videoUrl = "";
            _selectedImage = null;
            _displayName = "";
            _tags.Clear();
            _newTag = "";
            _generateError = "";
            _promptType = PromptType.Text;
            _model = MarbleModel.Plus;
            _isPublic = false;
        }

        #endregion

        #region Settings View

        private void DrawSettings()
        {
            EditorGUILayout.Space(10);

            // API Key Status
            EditorGUILayout.LabelField("API Configuration", _subHeaderStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key Status:", GUILayout.Width(100));
            EditorGUILayout.LabelField(_client.IsConfigured ? "✓ Configured" : "✗ Not Configured");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("API key is loaded from the .env file in your project root.\nSet WORLDLABS_API_KEY=your_key_here", MessageType.Info);

            if (GUILayout.Button("Open .env File"))
            {
                string envPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
                if (File.Exists(envPath))
                {
                    System.Diagnostics.Process.Start(envPath);
                }
                else
                {
                    EditorUtility.DisplayDialog("File Not Found", $"No .env file found at:\n{envPath}", "OK");
                }
            }

            if (GUILayout.Button("Reload API Key"))
            {
                EnvLoader.Reload();
                Initialize();
                Repaint();
            }

            EditorGUILayout.Space(10);
            DrawSeparator();
            EditorGUILayout.Space(10);

            // Output folder
            EditorGUILayout.LabelField("Import Settings", _subHeaderStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Folder:", GUILayout.Width(100));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(GetWorldsFolder());
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Choose Output Folder"))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string initialPath = Path.GetFullPath(Path.Combine(projectRoot, GetWorldsFolder()));
                string selectedPath = EditorUtility.OpenFolderPanel("Choose Output Folder", initialPath, "");
                if (TrySetWorldsFolderFromAbsolute(selectedPath))
                {
                    EnsureWorldsFolderExists();
                }
            }

            if (GUILayout.Button("Open Output Folder"))
            {
                EnsureWorldsFolderExists();
                EditorUtility.RevealInFinder(GetWorldsFolder());
            }
        }

        #endregion

        #region Initialization Error View

        private void DrawInitializationError()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(_initError, MessageType.Error);
            EditorGUILayout.Space(10);

            EditorGUILayout.HelpBox("To configure the API key:\n" +
                "1. Create a .env file in your project root (next to Assets folder)\n" +
                "2. Add: WORLDLABS_API_KEY=your_api_key_here\n" +
                "3. Click 'Retry' below", MessageType.Info);

            if (GUILayout.Button("Retry"))
            {
                EnvLoader.Reload();
                Initialize();
            }

            if (GUILayout.Button("Create .env File"))
            {
                CreateEnvFile();
            }
        }

        private void CreateEnvFile()
        {
            string envPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
            if (!File.Exists(envPath))
            {
                File.WriteAllText(envPath, "# WorldLabs API Configuration\nWORLDLABS_API_KEY=your_api_key_here\n");
                EditorUtility.DisplayDialog("Created", $".env file created at:\n{envPath}\n\nPlease add your API key.", "OK");
            }
            else
            {
                System.Diagnostics.Process.Start(envPath);
            }
        }

        #endregion

        #region API Operations

        private async void RefreshWorldsList()
        {
            // If already loading, queue a refresh for when current one finishes
            if (_isLoadingWorlds)
            {
                _pendingRefresh = true;
                return;
            }

            _isLoadingWorlds = true;
            _pendingRefresh = false;
            _loadError = null;
            _worlds.Clear();
            Repaint();

            try
            {
                MarbleModel? modelParam = _modelFilter == ModelFilter.All ? null : 
                    (_modelFilter == ModelFilter.Plus ? MarbleModel.Plus : MarbleModel.Mini);
                bool? visibilityParam = _visibilityFilter == VisibilityFilter.All ? null :
                    (_visibilityFilter == VisibilityFilter.Public ? (bool?)true : (bool?)false);
                string createdAfter = null;
                string createdBefore = null;
                if (string.IsNullOrEmpty(_currentPageToken))
                {
                    createdAfter = _createdAfterFilter;
                    createdBefore = _createdBeforeFilter;
                }
                
                if (DEBUG_API_CALLS)
                {
                    string windowText = createdAfter != null ? $", created_after={createdAfter}, created_before={createdBefore}" : "";
                    string visibilityText = visibilityParam.HasValue ? $", is_public={visibilityParam}" : "";
                    Debug.Log($"[WorldLabs] REQUEST at {DateTime.Now:HH:mm:ss.fff}: ListWorldsAsync(pageSize={PAGE_SIZE}, model={_modelFilter}, is_public={_visibilityFilter}, pageToken={_currentPageToken}{windowText}{visibilityText})");
                }

                var response = await _client.ListWorldsAsync(
                    PAGE_SIZE,
                    _currentPageToken,
                    null, // Status filter not needed - listed worlds are already complete
                    modelParam,
                    null, // tags filter - API support pending
                    null, // TODO: visibility filter - API support pending (visibilityParam)
                    _sortBy,
                    createdAfter,
                    createdBefore
                );

                if (DEBUG_API_CALLS)
                {
                    Debug.Log($"[WorldLabs] RESPONSE at {DateTime.Now:HH:mm:ss.fff}: Found {response.worlds?.Count ?? 0} worlds");
                    if (response.worlds != null && response.worlds.Count > 0)
                    {
                        var worldIds = string.Join(", ", response.worlds.ConvertAll(w => $"{w.display_name ?? w.world_id}"));
                        Debug.Log($"[WorldLabs] Worlds returned: {worldIds}");
                    }
                    Debug.Log($"[WorldLabs] RESPONSE RAW JSON:\n{JsonUtility.ToJson(response, true)}");
                }

                _nextPageToken = response.next_page_token;

                if (response.worlds != null)
                {
                    foreach (var world in response.worlds)
                    {
                        _worlds.Add(new WorldItem { World = world });
                    }
                }
            }
            catch (Exception ex)
            {
                _loadError = $"Failed to load worlds: {ex.Message}";
                if (DEBUG_API_CALLS) Debug.LogError($"[WorldLabs] API ERROR: {ex}");
            }
            finally
            {
                _isLoadingWorlds = false;
                Repaint();
                
                // If a refresh was requested while we were loading, do it now
                if (_pendingRefresh)
                {
                    _pendingRefresh = false;
                    RefreshWorldsList();
                }
            }
        }

        private void ResetPagination()
        {
            _currentPageToken = null;
            _nextPageToken = null;
            _pageTokenHistory.Clear();
            _currentPageIndex = 0;
            _pageTokenHistory.Add(null); // Page 1 token
        }

        private void SetRefreshTimeWindow()
        {
            DateTime now = DateTime.UtcNow;
            _createdAfterFilter = now.AddYears(-10).ToString("yyyy-MM-ddTHH:mm:ssZ");
            _createdBeforeFilter = now.AddYears(10).ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private void EnsureRefreshTimeWindow()
        {
            if (string.IsNullOrEmpty(_createdAfterFilter) || string.IsNullOrEmpty(_createdBeforeFilter))
            {
                SetRefreshTimeWindow();
            }
        }

        private void GoToNextPage()
        {
            if (string.IsNullOrEmpty(_nextPageToken)) return;

            if (_pageTokenHistory.Count == 0)
            {
                _pageTokenHistory.Add(null);
            }

            _currentPageIndex++;
            _currentPageToken = _nextPageToken;

            if (_pageTokenHistory.Count > _currentPageIndex)
            {
                _pageTokenHistory[_currentPageIndex] = _currentPageToken;
            }
            else
            {
                _pageTokenHistory.Add(_currentPageToken);
            }
            EnsureRefreshTimeWindow();
            RefreshWorldsList();
        }

        private void GoToPreviousPage()
        {
            if (_currentPageIndex <= 0) return;

            _currentPageIndex--;

            if (_pageTokenHistory.Count > _currentPageIndex)
            {
                _currentPageToken = _pageTokenHistory[_currentPageIndex];
            }
            else
            {
                _currentPageToken = null;
            }

            if (_currentPageIndex == 0)
            {
                _pageTokenHistory.Clear();
                _pageTokenHistory.Add(null);
            }
            
            EnsureRefreshTimeWindow();
            RefreshWorldsList();
        }

        private void DrawPaginationControls()
        {
            bool hasPreviousPage = _currentPageIndex > 0;
            bool hasNextPage = !string.IsNullOrEmpty(_nextPageToken);

            if (!hasPreviousPage && !hasNextPage) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(!hasPreviousPage);
            if (GUILayout.Button("← Previous", GUILayout.Width(100), GUILayout.Height(25)))
            {
                GoToPreviousPage();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);
            EditorGUILayout.LabelField($"Page {_currentPageIndex + 1}", _centerLabelStyle, GUILayout.Width(60));
            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(!hasNextPage);
            if (GUILayout.Button("Next →", GUILayout.Width(100), GUILayout.Height(25)))
            {
                GoToNextPage();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private async void StartGeneration()
        {
            _isGenerating = true;
            _generateError = "";
            Repaint();

            try
            {
                WorldsGenerateRequest request = BuildGenerateRequest();

                if (DEBUG_API_CALLS)
                {
                    Debug.Log($"[WorldLabs] REQUEST: GenerateWorldAsync");
                    Debug.Log($"  - display_name: {request.display_name}");
                    Debug.Log($"  - model: {request.model}");
                    Debug.Log($"  - tags: {(request.tags != null ? string.Join(", ", request.tags) : "(none)")}");
                    Debug.Log($"  - permission: {request.permission}");
                    Debug.Log($"  - prompt_type: {request.world_prompt?.GetType().Name}");
                    Debug.Log($"[WorldLabs] REQUEST RAW JSON:\n{JsonUtility.ToJson(request, true)}");
                }

                var response = await _client.GenerateWorldAsync(request);

                if (DEBUG_API_CALLS)
                {
                    Debug.Log($"[WorldLabs] RESPONSE: GenerateWorldAsync - operation_id={response.operation_id}");
                    Debug.Log($"[WorldLabs] RESPONSE RAW JSON:\n{JsonUtility.ToJson(response, true)}");
                }

                // Add to pending operations
                _pendingOperations[response.operation_id] = new PendingOperation
                {
                    OperationId = response.operation_id,
                    DisplayName = string.IsNullOrEmpty(_displayName) ? "New World" : _displayName,
                    StartTime = Time.realtimeSinceStartup,
                    Status = "Starting..."
                };

                // Switch to worlds list to show progress
                _currentView = ViewState.WorldsList;
                ResetCreateWorldForm();
            }
            catch (Exception ex)
            {
                _generateError = $"Failed to start generation: {ex.Message}";
                if (DEBUG_API_CALLS) Debug.LogError($"[WorldLabs] API ERROR: {ex}");
            }
            finally
            {
                _isGenerating = false;
                Repaint();
            }
        }

        private WorldsGenerateRequest BuildGenerateRequest()
        {
            WorldPrompt prompt;

            switch (_promptType)
            {
                case PromptType.Text:
                    prompt = TextPrompt.Create(_textPrompt);
                    break;

                case PromptType.ImageUrl:
                    prompt = ImagePrompt.FromUrl(_imageUrl, string.IsNullOrWhiteSpace(_textPrompt) ? null : _textPrompt);
                    break;

                case PromptType.ImageFile:
                    prompt = ImagePrompt.FromTexture(_selectedImage, "png", string.IsNullOrWhiteSpace(_textPrompt) ? null : _textPrompt);
                    break;

                case PromptType.VideoUrl:
                    prompt = VideoPrompt.FromUrl(_videoUrl, string.IsNullOrWhiteSpace(_textPrompt) ? null : _textPrompt);
                    break;

                default:
                    throw new Exception("Invalid prompt type");
            }

            return new WorldsGenerateRequest
            {
                world_prompt = prompt,
                display_name = string.IsNullOrWhiteSpace(_displayName) ? null : _displayName,
                model = _model == MarbleModel.Plus ? "Marble 0.1-plus" : "Marble 0.1-mini",
                tags = null, // TODO: tags - API support pending (_tags.Count > 0 ? _tags : null)
                permission = _isPublic ? Permission.Public : Permission.Private
            };
        }

        private async void PollPendingOperations()
        {
            var completedOps = new List<string>();

            foreach (var kvp in _pendingOperations)
            {
                var op = kvp.Value;
                if (op.IsComplete) continue;

                try
                {
                    if (DEBUG_API_CALLS) Debug.Log($"[WorldLabs] REQUEST: GetOperationAsync - operation_id={op.OperationId}");

                    var response = await _client.GetOperationAsync(op.OperationId);

                    if (DEBUG_API_CALLS)
                    {
                        Debug.Log($"[WorldLabs] RESPONSE: GetOperationAsync");
                        Debug.Log($"  - done: {response.done}");
                        Debug.Log($"  - progress: {response.metadata?.progress:P0}");
                        if (response.error != null) Debug.Log($"  - error: {response.error.message}");
                        Debug.Log($"[WorldLabs] RESPONSE RAW JSON:\n{JsonUtility.ToJson(response, true)}");
                    }

                    // Check if we can show preview (marble URL available)
                    if (!string.IsNullOrEmpty(response.response?.world_marble_url))
                    {
                        op.PreviewUrl = response.response.world_marble_url;
                    }

                    // Check if world generation is truly complete (has required assets)
                    bool isWorldComplete = response.done && IsWorldGenerationComplete(response.response);

                    if (isWorldComplete)
                    {
                        op.IsComplete = true;

                        if (response.error != null && !string.IsNullOrEmpty(response.error.message))
                        {
                            op.HasError = true;
                            op.ErrorMessage = response.error.message;
                        }
                        else
                        {
                            completedOps.Add(kvp.Key);
                            // Reset pagination so the newest world appears on the first page
                            SetRefreshTimeWindow();
                            ResetPagination();
                            RefreshWorldsList();
                        }
                    }
                    else if (response.done)
                    {
                        // Still waiting for assets to be ready
                        op.Status = "Finalizing assets...";
                    }
                    else
                    {
                        op.Progress = response.metadata?.progress ?? 0f;
                        op.Status = "Processing...";
                    }
                }
                catch (Exception ex)
                {
                    op.HasError = true;
                    op.ErrorMessage = ex.Message;
                    if (DEBUG_API_CALLS) Debug.LogError($"[WorldLabs] API ERROR: {ex}");
                }
            }

            // Remove completed successful operations
            foreach (var key in completedOps)
            {
                _pendingOperations.Remove(key);
            }

            Repaint();
        }

        private bool IsWorldGenerationComplete(World world)
        {
            if (world == null) return false;

            // World is complete when it has either panorama imagery or splats
            bool hasImagery = world.assets?.imagery != null && !string.IsNullOrEmpty(world.assets.imagery.pano_url);
            bool hasSplats = world.assets?.splats != null && world.assets.splats.spz_urls != null && world.assets.splats.spz_urls.Count > 0;

            return hasImagery || hasSplats;
        }

        private async void LoadThumbnail(WorldItem item)
        {
            // Prefer panorama URL (PNG) because thumbnails may be webp which Unity's DownloadHandlerTexture
            // can reject with a Data Processing Error. Fall back to thumbnail_url if pano is not available.
            string url = item.World.assets?.imagery?.pano_url ?? item.World.assets?.thumbnail_url;
            if (item.IsLoadingThumbnail || string.IsNullOrEmpty(url)) return;

            item.IsLoadingThumbnail = true;

            try
            {
                if (DEBUG_API_CALLS) Debug.Log($"[WorldLabs] REQUEST: DownloadTextureAsync - url={url}");
                item.Thumbnail = await WorldLabsClientExtensions.DownloadTextureAsync(url);
                if (DEBUG_API_CALLS) Debug.Log($"[WorldLabs] RESPONSE: Texture downloaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load preview from URL ({url}): {ex.Message}");
                if (DEBUG_API_CALLS) Debug.LogError($"[WorldLabs] API ERROR: {ex}");
            }
            finally
            {
                item.IsLoadingThumbnail = false;
                Repaint();
            }
        }

        private async void ImportWorld(WorldItem item, ImportFormat format)
        {
            item.IsImporting = true;
            item.ImportProgress = 0f;
            Repaint();

            EnsureWorldsFolderExists();

            string safeName = SanitizeFileName(item.World.display_name ?? item.World.world_id);
            string worldFolder = $"{GetWorldsFolder()}/{safeName}";

            if (!Directory.Exists(worldFolder))
            {
                Directory.CreateDirectory(worldFolder);
            }

            switch (format)
            {
                case ImportFormat.Mesh:
                    await ImportMesh(item, worldFolder);
                    item.ImportProgress = 1f;
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("Import Complete", $"World imported to:\n{worldFolder}", "OK");
                    EditorUtility.RevealInFinder(worldFolder);
                    break;

                case ImportFormat.GaussianSplat:
                    // Show import settings dialog
                    ShowCompressionQualityDialog((quality, loadToScene) =>
                    {
                        ImportGaussianSplatAsync(item, null, quality, loadToScene, worldFolder);
                    });
                    break;
            }
        }

        private async void ImportGaussianSplatAsync(WorldItem item, string resolution, CompressionQuality quality, bool loadToScene, string worldFolder = null)
        {
            try
            {
                if (worldFolder == null)
                {
                    EnsureWorldsFolderExists();
                    string safeName = SanitizeFileName(item.World.display_name ?? item.World.world_id);
                    worldFolder = $"{GetWorldsFolder()}/{safeName}";

                    if (!Directory.Exists(worldFolder))
                    {
                        Directory.CreateDirectory(worldFolder);
                    }
                }

                if (resolution == null)
                {
                    // Import all resolutions
                    await ImportGaussianSplat(item, worldFolder, quality);
                    item.ImportProgress = 1f;
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("Import Complete", $"World imported to:\n{worldFolder}", "OK");
                    EditorUtility.RevealInFinder(worldFolder);
                }
                else
                {
                    // Import specific resolution
                    GaussianSplatAsset importedAsset = await ImportGaussianSplatSingleResolution(item, worldFolder, resolution, quality);

                    item.ImportProgress = 1f;
                    AssetDatabase.Refresh();

                    if (loadToScene && importedAsset != null)
                    {
                        AddGaussianSplatToScene(importedAsset, SanitizeFileName(item.World.display_name ?? item.World.world_id));
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Import Complete", $"Gaussian Splat ({GetResolutionDisplayName(resolution)}) imported to:\n{worldFolder}", "OK");
                        EditorUtility.RevealInFinder(worldFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", $"Error: {ex.Message}", "OK");
            }
            finally
            {
                item.IsImporting = false;
                Repaint();
            }
        }

        private async Task ImportMesh(WorldItem item, string folder)
        {
            string meshUrl = item.World.assets.mesh.collider_mesh_url;
            string extension = GetExtensionFromUrl(meshUrl, "obj");
            string filePath = Path.Combine(folder, $"mesh.{extension}");

            item.ImportProgress = 0.2f;
            Repaint();

            byte[] data = await WorldLabsClientExtensions.DownloadBinaryAsync(meshUrl);

            item.ImportProgress = 0.8f;
            Repaint();

            File.WriteAllBytes(filePath, data);

            // Also download panorama if available
            if (item.World.assets.imagery?.pano_url != null)
            {
                try
                {
                    var panoData = await WorldLabsClientExtensions.DownloadBinaryAsync(item.World.assets.imagery.pano_url);
                    string panoPath = Path.Combine(folder, "panorama.jpg");
                    File.WriteAllBytes(panoPath, panoData);
                }
                catch { }
            }

            // Save world info
            SaveWorldInfo(item.World, folder);
        }

        private void ImportGaussianSplatWithResolution(WorldItem item, string resolution)
        {
            item.IsImporting = true;
            item.ImportProgress = 0f;
            Repaint();

            // Show import settings dialog and execute import on completion
            ShowCompressionQualityDialog((quality, loadToScene) =>
            {
                ImportGaussianSplatAsync(item, resolution, quality, loadToScene);
            });
        }

        private async Task<GaussianSplatAsset> ImportGaussianSplatSingleResolution(WorldItem item, string folder, string resolution, CompressionQuality quality)
        {
            // Normalize path for Unity (use forward slashes)
            folder = folder.Replace("\\", "/");
            
            string spzUrl = item.World.assets.splats.GetUrl(resolution);
            if (string.IsNullOrEmpty(spzUrl))
            {
                throw new Exception($"Resolution '{resolution}' not available for this world");
            }

            string fileName = $"splat_{resolution}.spz";
            string filePath = $"{folder}/{fileName}";

            item.ImportProgress = 0.2f;
            Repaint();

            byte[] data = await WorldLabsClientExtensions.DownloadBinaryAsync(spzUrl);

            item.ImportProgress = 0.8f;
            Repaint();

            File.WriteAllBytes(filePath, data);
            Debug.Log($"[WorldLabs] Downloaded Gaussian Splat ({resolution}): {filePath} ({data.Length / 1024f / 1024f:F2} MB)");

            // Use the quality setting provided
            GetFormatSettingsForQuality(quality, out var formatPos, out var formatScale, out var formatColor, out var formatSH);

            // Create the asset from the SPZ file
            string baseName = $"{folder.Substring(folder.LastIndexOf('/') + 1)}_{resolution}";
            GaussianSplatAsset asset = GaussianSplatAssetCreatorAPI.CreateAsset(
                filePath,
                folder,
                baseName,
                formatPos,
                formatScale,
                formatColor,
                formatSH,
                false
            );

            if (asset == null)
            {
                throw new Exception("Failed to create Gaussian Splat Asset from SPZ file");
            }

            // Also download panorama if available
            if (item.World.assets.imagery?.pano_url != null)
            {
                try
                {
                    item.ImportProgress = 0.9f;
                    Repaint();

                    var panoData = await WorldLabsClientExtensions.DownloadBinaryAsync(item.World.assets.imagery.pano_url);
                    string panoPath = Path.Combine(folder, "panorama.png");
                    File.WriteAllBytes(panoPath, panoData);
                }
                catch { }
            }

            // Save world info
            SaveWorldInfo(item.World, folder);
            
            return asset;
        }

        private async Task ImportGaussianSplat(WorldItem item, string folder, CompressionQuality quality)
        {
            // Normalize path for Unity (use forward slashes)
            folder = folder.Replace("\\", "/");
            
            // Download all available resolutions
            var spzUrls = item.World.assets.splats.spz_urls;
            if (spzUrls == null || spzUrls.Count == 0)
            {
                throw new Exception("No Gaussian Splat files available for this world");
            }

            int totalFiles = spzUrls.Count;
            int current = 0;
            string baseFolderName = folder.Substring(folder.LastIndexOf('/') + 1);

            foreach (var kvp in spzUrls)
            {
                string fileName = $"splat_{SanitizeFileName(kvp.Key)}.spz";
                string filePath = $"{folder}/{fileName}";

                byte[] data = await WorldLabsClientExtensions.DownloadBinaryAsync(kvp.Value);
                File.WriteAllBytes(filePath, data);
                Debug.Log($"[WorldLabs] Downloaded Gaussian Splat ({kvp.Key}): {filePath} ({data.Length / 1024f / 1024f:F2} MB)");

                // Use the quality setting provided
                GetFormatSettingsForQuality(quality, out var formatPos, out var formatScale, out var formatColor, out var formatSH);

                // Create the asset from the SPZ file
                string baseName = $"{baseFolderName}_{kvp.Key}";
                GaussianSplatAsset asset = GaussianSplatAssetCreatorAPI.CreateAsset(
                    filePath,
                    folder,
                    baseName,
                    formatPos,
                    formatScale,
                    formatColor,
                    formatSH,
                    false
                );

                if (asset == null)
                {
                    Debug.LogWarning($"[WorldLabs] Failed to create asset for resolution {kvp.Key}");
                }

                current++;
                item.ImportProgress = (float)current / totalFiles * 0.9f;
                Repaint();
            }

            // Also download panorama if available
            if (item.World.assets.imagery?.pano_url != null)
            {
                try
                {
                    var panoData = await WorldLabsClientExtensions.DownloadBinaryAsync(item.World.assets.imagery.pano_url);
                    string panoPath = Path.Combine(folder, "panorama.png");
                    File.WriteAllBytes(panoPath, panoData);
                }
                catch { }
            }

            // Save world info
            SaveWorldInfo(item.World, folder);
        }

        private void SaveWorldInfo(World world, string folder)
        {
            var info = new WorldInfoFile
            {
                world_id = world.world_id,
                display_name = world.display_name,
                world_marble_url = world.world_marble_url,
                model = world.model,
                caption = world.assets?.caption,
                created_at = world.created_at,
                imported_at = DateTime.Now.ToString("o")
            };

            string json = JsonUtility.ToJson(info, true);
            File.WriteAllText(Path.Combine(folder, "world_info.json"), json);
        }

        #endregion

        #region Load To Scene

        private void AddGaussianSplatToScene(GaussianSplatAsset asset, string worldName)
        {
            // Ensure URP Renderer Feature is configured
            EnsureGaussianSplatURPFeature();
            
            // Find existing GaussianSplatRenderer in scene
            GaussianSplatRenderer existingRenderer = FindFirstObjectByType<GaussianSplatRenderer>();

            if (existingRenderer != null)
            {
                // Use existing renderer - update its asset
                existingRenderer.m_Asset = asset;
                EditorUtility.SetDirty(existingRenderer);
                Selection.activeGameObject = existingRenderer.gameObject;
                Debug.Log($"[WorldLabs] Updated existing GaussianSplatRenderer with new asset");
            }
            else
            {
                // Create new GameObject with GaussianSplatRenderer
                GameObject splatObject = new GameObject($"GaussianSplat_{worldName}");
                
                // Set rotation to (-180, 0, 0) as required
                splatObject.transform.rotation = Quaternion.Euler(-180f, 0f, 0f);

                // Add GaussianSplatRenderer component (OnEnable will run but shaders are null)
                GaussianSplatRenderer renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                
                // Assign required shaders
                const string shaderBasePath = "Assets/GaussianSPlatting/Shaders/";
                renderer.m_ShaderSplats = AssetDatabase.LoadAssetAtPath<Shader>(shaderBasePath + "RenderGaussianSplats.shader");
                renderer.m_ShaderComposite = AssetDatabase.LoadAssetAtPath<Shader>(shaderBasePath + "GaussianComposite.shader");
                renderer.m_ShaderDebugPoints = AssetDatabase.LoadAssetAtPath<Shader>(shaderBasePath + "GaussianDebugRenderPoints.shader");
                renderer.m_ShaderDebugBoxes = AssetDatabase.LoadAssetAtPath<Shader>(shaderBasePath + "GaussianDebugRenderBoxes.shader");
                renderer.m_CSSplatUtilities_deviceRadixSort = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderBasePath + "SplatUtilities_DeviceRadixSort.compute");
                renderer.m_CSSplatUtilities_fidelityFX = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderBasePath + "SplatUtilities_FidelityFX.compute");
                
                // Assign asset
                renderer.m_Asset = asset;
                
                // Re-enable to trigger proper initialization now that shaders are assigned
                renderer.enabled = false;
                renderer.enabled = true;

                // Register undo
                Undo.RegisterCreatedObjectUndo(splatObject, "Create Gaussian Splat Object");

                Selection.activeGameObject = splatObject;
                Debug.Log($"[WorldLabs] Created new GaussianSplatRenderer GameObject with rotation (-180, 0, 0)");
            }

            // Mark scene as dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        
        private void EnsureGaussianSplatURPFeature()
        {
#if GS_ENABLE_URP
            // Get the current URP pipeline asset
            var pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset == null)
            {
                Debug.LogWarning("[WorldLabs] No URP Pipeline Asset found. Gaussian Splat may not render correctly.");
                return;
            }

            // Get the scriptable renderer data using reflection (Unity doesn't expose this directly)
            var propertyInfo = typeof(UniversalRenderPipelineAsset).GetProperty("scriptableRendererData", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (propertyInfo == null)
            {
                // Try alternative field name
                var fieldInfo = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (fieldInfo != null)
                {
                    var rendererDataList = fieldInfo.GetValue(pipelineAsset) as ScriptableRendererData[];
                    if (rendererDataList != null)
                    {
                        foreach (var rendererData in rendererDataList)
                        {
                            if (rendererData != null)
                            {
                                AddGaussianSplatFeatureToRenderer(rendererData);
                            }
                        }
                    }
                }
                return;
            }
            
            var scriptableRendererData = propertyInfo.GetValue(pipelineAsset) as ScriptableRendererData;
            if (scriptableRendererData != null)
            {
                AddGaussianSplatFeatureToRenderer(scriptableRendererData);
            }
#endif
        }

#if GS_ENABLE_URP
        private void AddGaussianSplatFeatureToRenderer(ScriptableRendererData rendererData)
        {
            // Check if feature already exists
            foreach (var existingFeature in rendererData.rendererFeatures)
            {
                if (existingFeature != null && existingFeature.GetType().Name == "GaussianSplatURPFeature")
                {
                    return; // Already added
                }
            }
            
            // Add the feature
            var featureType = System.Type.GetType("GaussianSplatting.Runtime.GaussianSplatURPFeature, GaussianSplatting");
            if (featureType == null)
            {
                Debug.LogWarning("[WorldLabs] Could not find GaussianSplatURPFeature type. Please add it manually to your URP Renderer.");
                return;
            }
            
            var newFeature = ScriptableObject.CreateInstance(featureType) as ScriptableRendererFeature;
            if (newFeature == null)
            {
                Debug.LogWarning("[WorldLabs] Could not create GaussianSplatURPFeature instance.");
                return;
            }
            
            newFeature.name = "GaussianSplatURPFeature";
            
            // Add to asset
            AssetDatabase.AddObjectToAsset(newFeature, rendererData);
            
            // Add to renderer features list using reflection
            var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (featuresField != null)
            {
                var featuresList = featuresField.GetValue(rendererData) as List<ScriptableRendererFeature>;
                if (featuresList != null)
                {
                    featuresList.Add(newFeature);
                    featuresField.SetValue(rendererData, featuresList);
                }
            }
            
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"[WorldLabs] Added GaussianSplatURPFeature to {rendererData.name}");
        }
#endif

        #endregion

        #region Compression Quality Helpers

        private CompressionQuality ShowCompressionQualityDialog(System.Action<CompressionQuality, bool> onComplete)
        {
            _selectedCompressionQuality = CompressionQuality.Medium;
            _loadToScene = true;
            _importSettingsDialog = ScriptableObject.CreateInstance<ImportSettingsDialog>();
            _importSettingsDialog.SetOnCompleteCallback(onComplete);
            _importSettingsDialog.ShowUtility();
            return _selectedCompressionQuality;
        }

        private void GetFormatSettingsForQuality(
            CompressionQuality quality,
            out GaussianSplatAsset.VectorFormat formatPos,
            out GaussianSplatAsset.VectorFormat formatScale,
            out GaussianSplatAsset.ColorFormat formatColor,
            out GaussianSplatAsset.SHFormat formatSH)
        {
            switch (quality)
            {
                case CompressionQuality.VeryHigh:
                    formatPos = GaussianSplatAsset.VectorFormat.Float32;
                    formatScale = GaussianSplatAsset.VectorFormat.Float32;
                    formatColor = GaussianSplatAsset.ColorFormat.Float32x4;
                    formatSH = GaussianSplatAsset.SHFormat.Float32;
                    break;
                case CompressionQuality.High:
                    formatPos = GaussianSplatAsset.VectorFormat.Norm16;
                    formatScale = GaussianSplatAsset.VectorFormat.Norm16;
                    formatColor = GaussianSplatAsset.ColorFormat.Float16x4;
                    formatSH = GaussianSplatAsset.SHFormat.Norm11;
                    break;
                case CompressionQuality.Medium:
                    formatPos = GaussianSplatAsset.VectorFormat.Norm11;
                    formatScale = GaussianSplatAsset.VectorFormat.Norm11;
                    formatColor = GaussianSplatAsset.ColorFormat.Norm8x4;
                    formatSH = GaussianSplatAsset.SHFormat.Norm6;
                    break;
                case CompressionQuality.Low:
                    formatPos = GaussianSplatAsset.VectorFormat.Norm11;
                    formatScale = GaussianSplatAsset.VectorFormat.Norm6;
                    formatColor = GaussianSplatAsset.ColorFormat.Norm8x4;
                    formatSH = GaussianSplatAsset.SHFormat.Cluster16k;
                    break;
                case CompressionQuality.VeryLow:
                    formatPos = GaussianSplatAsset.VectorFormat.Norm11;
                    formatScale = GaussianSplatAsset.VectorFormat.Norm6;
                    formatColor = GaussianSplatAsset.ColorFormat.BC7;
                    formatSH = GaussianSplatAsset.SHFormat.Cluster4k;
                    break;
                default:
                    formatPos = GaussianSplatAsset.VectorFormat.Norm11;
                    formatScale = GaussianSplatAsset.VectorFormat.Norm11;
                    formatColor = GaussianSplatAsset.ColorFormat.Norm8x4;
                    formatSH = GaussianSplatAsset.SHFormat.Norm6;
                    break;
            }
        }

        #endregion

        #region Utility Methods

        private void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space(2);
        }

        private void EnsureWorldsFolderExists()
        {
            string worldsFolder = GetWorldsFolder();
            if (!Directory.Exists(worldsFolder))
            {
                Directory.CreateDirectory(worldsFolder);
                AssetDatabase.Refresh();
            }
        }

        private string LoadWorldsFolder()
        {
            string stored = EditorPrefs.GetString(WORLDS_FOLDER_PREF_KEY, DEFAULT_WORLDS_FOLDER);
            return NormalizeAssetPath(string.IsNullOrWhiteSpace(stored) ? DEFAULT_WORLDS_FOLDER : stored);
        }

        private string GetWorldsFolder()
        {
            if (string.IsNullOrWhiteSpace(_worldsFolder))
            {
                _worldsFolder = LoadWorldsFolder();
            }

            return _worldsFolder;
        }

        private void SaveWorldsFolder(string assetPath)
        {
            _worldsFolder = NormalizeAssetPath(assetPath);
            EditorPrefs.SetString(WORLDS_FOLDER_PREF_KEY, _worldsFolder);
        }

        private string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return DEFAULT_WORLDS_FOLDER;
            }

            return path.Replace("\\", "/").TrimEnd('/');
        }

        private bool TrySetWorldsFolderFromAbsolute(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets"));
            string selectedFullPath = Path.GetFullPath(absolutePath);

            if (!selectedFullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Folder",
                    "Please choose a folder inside the project's Assets directory so Unity can import the files.",
                    "OK");
                return false;
            }

            string relativeSuffix = selectedFullPath.Substring(assetsRoot.Length).Replace("\\", "/");
            string assetPath = "Assets" + relativeSuffix;
            SaveWorldsFolder(assetPath);
            return true;
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "world";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        private string GetExtensionFromUrl(string url, string defaultExt)
        {
            try
            {
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string ext = Path.GetExtension(path).TrimStart('.');
                return string.IsNullOrEmpty(ext) ? defaultExt : ext;
            }
            catch
            {
                return defaultExt;
            }
        }

        private string FormatDateTime(string isoDateTime)
        {
            try
            {
                if (DateTime.TryParse(isoDateTime, out DateTime dateTime))
                {
                    return dateTime.ToString("MMM dd, yyyy HH:mm");
                }
            }
            catch
            {
                // Fall through to return original
            }
            return isoDateTime;
        }

        #endregion

        #region Helper Classes

        [Serializable]
        private class WorldInfoFile
        {
            public string world_id;
            public string display_name;
            public string world_marble_url;
            public string model;
            public string caption;
            public string created_at;
            public string imported_at;
        }

        #endregion
    }

    /// <summary>
    /// Dialog for selecting Gaussian Splat import settings (compression quality and load to scene)
    /// </summary>
    public class ImportSettingsDialog : EditorWindow
    {
        private GUIStyle _titleStyle;
        private GUIStyle _descriptionStyle;
        private Vector2 _scrollPosition;
        private System.Action<CompressionQuality, bool> _onComplete;

        public void SetOnCompleteCallback(System.Action<CompressionQuality, bool> callback)
        {
            _onComplete = callback;
        }

        private void OnGUI()
        {
            InitializeStyles();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Space(10);
            GUILayout.Label("Gaussian Splat Import Settings", _titleStyle, GUILayout.Height(25));
            GUILayout.Space(5);
            
            GUILayout.Label("Select compression quality for the Gaussian Splat asset:", _descriptionStyle);
            GUILayout.Space(10);

            // Quality options
            if (DrawQualityButton("Very High", CompressionQuality.VeryHigh, "1.05x smaller - Best quality, largest file"))
                SelectQuality(CompressionQuality.VeryHigh);
            
            if (DrawQualityButton("High", CompressionQuality.High, "2.94x smaller - 57.77 PSNR"))
                SelectQuality(CompressionQuality.High);
            
            if (DrawQualityButton("Medium", CompressionQuality.Medium, "5.14x smaller - 47.46 PSNR (recommended)", true))
                SelectQuality(CompressionQuality.Medium);
            
            if (DrawQualityButton("Low", CompressionQuality.Low, "14.01x smaller - 35.17 PSNR"))
                SelectQuality(CompressionQuality.Low);
            
            if (DrawQualityButton("Very Low", CompressionQuality.VeryLow, "18.62x smaller - 32.27 PSNR - Smallest file"))
                SelectQuality(CompressionQuality.VeryLow);

            GUILayout.Space(15);
            EditorGUILayout.HelpBox("PSNR (Peak Signal-to-Noise Ratio) measures visual quality. Higher is better.", MessageType.Info);

            GUILayout.Space(15);
            GUILayout.Label("After Import", _titleStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            WorldLabsEditorWindow._loadToScene = EditorGUILayout.Toggle("Load to Scene", WorldLabsEditorWindow._loadToScene);
            
            GUILayout.Space(5);
            EditorGUILayout.HelpBox("If enabled, the Gaussian Splat will be added to the active scene. Otherwise, the folder will open in Explorer.", MessageType.Info);

            GUILayout.Space(15);
            
            if (GUILayout.Button("Done", GUILayout.Height(35)))
            {
                CompleteAndClose();
            }

            GUILayout.EndScrollView();
        }

        private void InitializeStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 5, 5)
            };

            _descriptionStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                margin = new RectOffset(5, 5, 0, 10)
            };
        }

        private bool DrawQualityButton(string title, CompressionQuality quality, string description, bool isDefault = false)
        {
            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(40));
            
            bool isSelected = WorldLabsEditorWindow._selectedCompressionQuality == quality;
            
            // Draw background - highlight if selected
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.4f, 0.7f, 1f); // Blue highlight for selected
            }
            else if (isDefault)
            {
                GUI.backgroundColor = new Color(1, 1, 0.9f); // Light yellow for recommended
            }
            else
            {
                GUI.backgroundColor = Color.white;
            }
            GUI.Box(rect, "", EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            // Draw selection indicator
            string indicator = isSelected ? "● " : "○ ";
            
            // Draw content
            Rect titleRect = new Rect(rect.x + 10, rect.y + 5, rect.width - 20, 18);
            GUI.Label(titleRect, indicator + title, EditorStyles.boldLabel);

            Rect descRect = new Rect(rect.x + 10, rect.y + 23, rect.width - 20, 15);
            GUI.Label(descRect, description, EditorStyles.miniLabel);

            // Check if clicked
            return GUI.Button(rect, "", GUIStyle.none);
        }

        private void SelectQuality(CompressionQuality quality)
        {
            WorldLabsEditorWindow._selectedCompressionQuality = quality;
            Repaint();
        }

        private void CompleteAndClose()
        {
            _onComplete?.Invoke(WorldLabsEditorWindow._selectedCompressionQuality, WorldLabsEditorWindow._loadToScene);
            Close();
        }
    }
}

