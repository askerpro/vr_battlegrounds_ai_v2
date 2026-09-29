// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.Tools
{
    public class KPackageUpdateTool : IEditorTool
    {
        private const string ManifestFileName = "kinemation-package.json";
        private const float ActionButtonWidth = 116f;
        private static readonly string DownloadDirectory = Path.Combine("Library", "KINEMATION", "Packages");
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static Color CardColor = EditorGUIUtility.isProSkin
            ? new Color32(52, 52, 52, 255)
            : new Color32(210, 210, 210, 255);
        public static Color BackgroundColor = EditorGUIUtility.isProSkin
            ? new Color32(56, 56, 56, 255)
            : new Color32(128, 128, 128, 255);

        private List<PackageEntry> _packages = new List<PackageEntry>();
        private CancellationTokenSource _refreshCancellation;
        private CancellationTokenSource _operationCancellation;
        private PackageEntry _operationEntry;
        private long _downloadedBytes;
        private long _downloadTotalBytes;
        private string _downloadLabel;
        private bool _refreshing;
        private int _refreshGeneration;
        private int _operationIndex;
        private int _operationCount;
        private OperationPhase _operationPhase;
        private Vector2 _scrollPosition;

        private GUIStyle _summaryStyle;
        private GUIStyle _backgroundStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _packageNameStyle;
        private GUIStyle _packagePathStyle;
        private GUIStyle _versionStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _statusDotStyle;
        private GUIStyle _releaseNotesStyle;
        private Texture2D _backgroundTexture;
        private Texture2D _cardTexture;
        private Color _appliedBackgroundColor;
        private Color _appliedCardColor;

        public void Init()
        {
            Refresh();
        }

        public void Render()
        {
            EnsureStyles();
            UpdateColorStyles();
            EditorGUILayout.BeginVertical(_backgroundStyle, GUILayout.ExpandHeight(true));

            DrawSummary();
            EditorGUILayout.Space(8f);

            if (_packages.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No {ManifestFileName} files were found under Assets/KINEMATION.",
                    MessageType.Info);
            }
            else
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                foreach (var package in _packages)
                {
                    DrawPackage(package);
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        public string GetToolCategory()
        {
            return "General/";
        }

        public string GetToolName()
        {
            return "Update Packages";
        }

        public string GetDocsURL()
        {
            return string.Empty;
        }

        public string GetToolDescription()
        {
            return "Update installed KINEMATION dependencies with this tool.";
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private bool IsOperationActive => _operationPhase != OperationPhase.None;

        private int UpdateCount => _packages.Count(package =>
            package.remoteState == RemoteState.Ready &&
            package.versionRelation == VersionRelation.RemoteNewer);

        private void EnsureStyles()
        {
            if (_cardStyle != null) return;

            _summaryStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                clipping = TextClipping.Clip
            };

            _backgroundStyle = new GUIStyle(GUIStyle.none);

            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 8),
                margin = new RectOffset(0, 0, 0, 8),
                border = new RectOffset(0, 0, 0, 0)
            };

            _packageNameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };

            _packagePathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip
            };

            _versionStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12
            };

            _statusStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12
            };

            _statusDotStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                richText = true
            };

            _releaseNotesStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(18, 2, 0, 0)
            };
        }

        private void UpdateColorStyles()
        {
            if (_backgroundTexture == null)
            {
                _backgroundTexture = CreateColorTexture(1, 1);
                _backgroundStyle.normal.background = _backgroundTexture;
                _appliedBackgroundColor = new Color(-1f, -1f, -1f, -1f);
            }

            if (_cardTexture == null)
            {
                _cardTexture = CreateColorTexture(1, 1);
                _cardStyle.normal.background = _cardTexture;
                _appliedCardColor = new Color(-1f, -1f, -1f, -1f);
            }

            if (_appliedBackgroundColor != BackgroundColor)
            {
                _backgroundTexture.SetPixel(0, 0, BackgroundColor);
                _backgroundTexture.Apply(false, false);
                _appliedBackgroundColor = BackgroundColor;
            }

            if (_appliedCardColor != CardColor)
            {
                _cardTexture.SetPixel(0, 0, CardColor);
                _cardTexture.Apply(false, false);
                _appliedCardColor = CardColor;
            }
        }

        private static Texture2D CreateColorTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private void DrawSummary()
        {
            int updateCount = UpdateCount;
            string installedText = $"{_packages.Count} {Pluralize(_packages.Count, "package", "packages")} installed";
            string updateText = _refreshing
                ? "Checking for updates..."
                : $"{updateCount} {Pluralize(updateCount, "update", "updates")} available";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{installedText}  \u2022  {updateText}", _summaryStyle,
                GUILayout.ExpandWidth(true));

            if (updateCount > 0)
            {
                using (new EditorGUI.DisabledScope(_refreshing || IsOperationActive))
                {
                    if (GUILayout.Button("Update All", GUILayout.Width(92f), GUILayout.Height(24f)))
                    {
                        StartOperation(_packages.Where(package =>
                            package.remoteState == RemoteState.Ready &&
                            package.versionRelation == VersionRelation.RemoteNewer));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(_refreshing || IsOperationActive))
            {
                if (GUILayout.Button(_refreshing ? "Checking..." : "Check for Updates",
                        GUILayout.Width(128f), GUILayout.Height(24f)))
                {
                    Refresh();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string Pluralize(int count, string singular, string plural)
        {
            return count == 1 ? singular : plural;
        }

        private async void Refresh()
        {
            if (IsOperationActive) return;

            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = new CancellationTokenSource();

            int generation = ++_refreshGeneration;
            CancellationToken cancellationToken = _refreshCancellation.Token;

            var releaseNotesStates = _packages
                .Where(package => package.releaseNotesInitialized)
                .ToDictionary(package => package.manifestPath, package => package.releaseNotesExpanded,
                    StringComparer.OrdinalIgnoreCase);
            var previousOrder = _packages
                .Select((package, index) => new { package.manifestPath, index })
                .ToDictionary(entry => entry.manifestPath, entry => entry.index,
                    StringComparer.OrdinalIgnoreCase);

            _packages = DiscoverPackages();
            _packages.Sort((left, right) =>
            {
                bool hasLeftIndex = previousOrder.TryGetValue(left.manifestPath, out int leftIndex);
                bool hasRightIndex = previousOrder.TryGetValue(right.manifestPath, out int rightIndex);

                if (hasLeftIndex && hasRightIndex) return leftIndex.CompareTo(rightIndex);
                if (hasLeftIndex) return -1;
                if (hasRightIndex) return 1;
                return string.Compare(left.manifestPath, right.manifestPath,
                    StringComparison.OrdinalIgnoreCase);
            });

            foreach (var package in _packages)
            {
                if (!releaseNotesStates.TryGetValue(package.manifestPath, out bool expanded)) continue;
                package.releaseNotesExpanded = expanded;
                package.releaseNotesInitialized = true;
            }

            _refreshing = true;
            Repaint();

            try
            {
                var fetches = _packages
                    .Where(package => package.localManifest != null && package.remoteState != RemoteState.Invalid)
                    .Select(package => FetchRemoteManifest(package, generation, cancellationToken));

                await Task.WhenAll(fetches);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (generation == _refreshGeneration)
                {
                    SortPackages();
                    _refreshing = false;
                    Repaint();
                }
            }
        }

        private static List<PackageEntry> DiscoverPackages()
        {
            var packages = new List<PackageEntry>();
            string rootPath = Path.Combine(Application.dataPath, "KINEMATION");

            if (!Directory.Exists(rootPath))
            {
                return packages;
            }

            string[] manifestPaths = Directory
                .GetFiles(rootPath, ManifestFileName, SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(path), ManifestFileName, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string manifestPath in manifestPaths)
            {
                string packageRoot = Path.GetDirectoryName(manifestPath);
                var package = new PackageEntry
                {
                    manifestPath = manifestPath,
                    packageRoot = packageRoot,
                    packageRootAssetPath = ToAssetPath(packageRoot),
                    fallbackName = Path.GetFileName(packageRoot),
                    remoteState = RemoteState.Pending,
                    status = "Checking for updates...",
                    statusKind = PackageStatusKind.Checking
                };

                try
                {
                    string json = File.ReadAllText(manifestPath);
                    if (!TryParseLocalManifest(json, out package.localManifest, out package.localVersion,
                            out string error))
                    {
                        package.remoteState = RemoteState.Invalid;
                        package.status = error;
                        package.statusKind = PackageStatusKind.Error;
                        package.releaseNotesInitialized = true;
                    }
                }
                catch (Exception exception)
                {
                    package.remoteState = RemoteState.Invalid;
                    package.status = $"Could not read the local manifest: {exception.Message}";
                    package.statusKind = PackageStatusKind.Error;
                    package.releaseNotesInitialized = true;
                }

                packages.Add(package);
            }

            var packageIds = new Dictionary<string, List<PackageEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages.Where(package => package.localManifest != null))
            {
                string packageId = package.localManifest.packageId;
                if (!packageIds.TryGetValue(packageId, out var matches))
                {
                    matches = new List<PackageEntry>();
                    packageIds.Add(packageId, matches);
                }

                matches.Add(package);
            }

            foreach (var duplicate in packageIds.Where(pair => pair.Value.Count > 1))
            {
                foreach (var package in duplicate.Value)
                {
                    package.remoteState = RemoteState.Invalid;
                    package.status = $"Duplicate package ID '{duplicate.Key}'.";
                    package.statusKind = PackageStatusKind.Error;
                    package.releaseNotesInitialized = true;
                }
            }

            return packages;
        }

        private async Task FetchRemoteManifest(PackageEntry package, int generation,
            CancellationToken cancellationToken)
        {
            package.status = "Checking for updates...";
            package.statusKind = PackageStatusKind.Checking;
            Repaint();

            try
            {
                using (HttpResponseMessage response = await HttpClient.GetAsync(
                           package.localManifest.manifestUrl,
                           HttpCompletionOption.ResponseContentRead,
                           cancellationToken))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!response.IsSuccessStatusCode)
                    {
                        package.remoteState = RemoteState.Invalid;
                        package.status =
                            $"Remote manifest returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
                        package.statusKind = PackageStatusKind.Error;
                        return;
                    }

                    if (!TryParseRemoteManifest(json, package.localManifest.packageId,
                            out package.remoteManifest, out package.remoteVersion, out string error))
                    {
                        package.remoteState = RemoteState.Invalid;
                        package.status = error;
                        package.statusKind = PackageStatusKind.Error;
                        return;
                    }

                    int comparison = package.remoteVersion.CompareTo(package.localVersion);
                    package.remoteState = RemoteState.Ready;

                    if (comparison > 0)
                    {
                        package.versionRelation = VersionRelation.RemoteNewer;
                        package.status = "Update available";
                        package.statusKind = PackageStatusKind.UpdateAvailable;
                        InitializeReleaseNotes(package, true);
                    }
                    else if (comparison == 0)
                    {
                        package.versionRelation = VersionRelation.Equal;
                        package.status = "Up to date";
                        package.statusKind = PackageStatusKind.UpToDate;
                    }
                    else
                    {
                        package.versionRelation = VersionRelation.RemoteOlder;
                        package.status = "Installed version is newer";
                        package.statusKind = PackageStatusKind.Warning;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                package.remoteState = RemoteState.Invalid;
                package.status = "The remote manifest request timed out.";
                package.statusKind = PackageStatusKind.Error;
            }
            catch (Exception exception)
            {
                package.remoteState = RemoteState.Invalid;
                package.status = $"Could not fetch the remote manifest: {exception.Message}";
                package.statusKind = PackageStatusKind.Error;
            }
            finally
            {
                InitializeReleaseNotes(package, false);
                if (generation == _refreshGeneration)
                {
                    Repaint();
                }
            }
        }

        private static void InitializeReleaseNotes(PackageEntry package, bool expanded)
        {
            if (package.releaseNotesInitialized) return;
            package.releaseNotesExpanded = expanded;
            package.releaseNotesInitialized = true;
        }

        private void SortPackages()
        {
            _packages.Sort((left, right) =>
            {
                int comparison = GetSortOrder(left).CompareTo(GetSortOrder(right));
                return comparison != 0
                    ? comparison
                    : string.Compare(left.DisplayName, right.DisplayName,
                        StringComparison.OrdinalIgnoreCase);
            });
        }

        private static int GetSortOrder(PackageEntry package)
        {
            switch (package.statusKind)
            {
                case PackageStatusKind.UpdateAvailable:
                    return 0;
                case PackageStatusKind.Warning:
                case PackageStatusKind.Error:
                    return 1;
                case PackageStatusKind.UpToDate:
                    return 2;
                default:
                    return 3;
            }
        }

        private void DrawPackage(PackageEntry package)
        {
            Rect cardRect = EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(package.DisplayName, _packageNameStyle);
            EditorGUILayout.LabelField(package.packageRootAssetPath, _packagePathStyle);
            EditorGUILayout.Space(7f);
            DrawVersionAndStatus(package);
            EditorGUILayout.EndVertical();

            GUILayout.Space(12f);
            EditorGUILayout.BeginVertical(GUILayout.Width(ActionButtonWidth));
            GUILayout.Space(20f);
            DrawPackageAction(package);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(package.operationMessage))
            {
                EditorGUILayout.HelpBox(package.operationMessage, package.operationMessageType);
            }

            if (_operationEntry == package && _operationPhase != OperationPhase.None)
            {
                EditorGUILayout.Space(4f);
                DrawOperationProgress();
            }

            EditorGUILayout.Space(7f);
            DrawSeparator();
            EditorGUILayout.Space(3f);
            DrawReleaseNotes(package);
            EditorGUILayout.EndVertical();
            DrawCardBorder(cardRect);
        }

        private static void DrawCardBorder(Rect cardRect)
        {
            Color borderColor = Color.Lerp(CardColor, new Color32(25,25,25, 255),
                EditorGUIUtility.isProSkin ? 0.32f : 0.18f);
            borderColor.a = CardColor.a;

            const float borderWidth = 1.05f;
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, borderWidth), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.yMax - borderWidth, cardRect.width, borderWidth), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, borderWidth, cardRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.xMax - borderWidth, cardRect.y, borderWidth, cardRect.height), borderColor);
        }

        private void DrawVersionAndStatus(PackageEntry package)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetVersionDisplay(package), _versionStyle, GUILayout.Width(150f));
            GUILayout.Label(GetStatusDot(package.statusKind), _statusDotStyle, GUILayout.Width(15f));
            EditorGUILayout.LabelField(package.status, _statusStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private static string GetVersionDisplay(PackageEntry package)
        {
            if (package.localManifest == null) return "Invalid manifest";

            if (package.remoteState == RemoteState.Ready &&
                package.versionRelation == VersionRelation.RemoteNewer)
            {
                return $"{package.localManifest.version}  \u2192  {package.remoteManifest.version}";
            }

            return package.localManifest.version;
        }

        private static string GetStatusDot(PackageStatusKind statusKind)
        {
            Color color;
            switch (statusKind)
            {
                case PackageStatusKind.UpdateAvailable:
                    color = new Color(0.27f, 0.55f, 0.95f);
                    break;
                case PackageStatusKind.UpToDate:
                    color = new Color(0.28f, 0.76f, 0.37f);
                    break;
                case PackageStatusKind.Warning:
                    color = new Color(0.95f, 0.67f, 0.22f);
                    break;
                case PackageStatusKind.Error:
                    color = new Color(0.9f, 0.3f, 0.3f);
                    break;
                default:
                    color = new Color(0.55f, 0.58f, 0.62f);
                    break;
            }

            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>\u25CF</color>";
        }

        private void DrawPackageAction(PackageEntry package)
        {
            if (_operationEntry == package)
            {
                if (_operationPhase == OperationPhase.Downloading)
                {
                    bool cancelling = _operationCancellation == null ||
                                      _operationCancellation.IsCancellationRequested;
                    using (new EditorGUI.DisabledScope(cancelling))
                    {
                        if (GUILayout.Button(cancelling ? "Cancelling..." : "Cancel",
                                GUILayout.Width(ActionButtonWidth), GUILayout.Height(26f)))
                        {
                            _downloadLabel = "Cancelling...";
                            _operationCancellation?.Cancel();
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button("Importing...", GUILayout.Width(ActionButtonWidth),
                            GUILayout.Height(26f));
                    }
                }
                return;
            }

            string label;
            bool canInstall = package.remoteState == RemoteState.Ready &&
                              (package.versionRelation == VersionRelation.RemoteNewer ||
                               package.versionRelation == VersionRelation.Equal);

            if (package.remoteState == RemoteState.Pending)
            {
                label = "Checking...";
            }
            else if (package.versionRelation == VersionRelation.RemoteNewer)
            {
                label = "Update";
            }
            else if (package.versionRelation == VersionRelation.Equal)
            {
                label = "Reinstall";
            }
            else
            {
                label = "Unavailable";
            }

            using (new EditorGUI.DisabledScope(!canInstall || IsOperationActive || _refreshing))
            {
                if (GUILayout.Button(label, GUILayout.Width(ActionButtonWidth), GUILayout.Height(26f)))
                {
                    StartOperation(new[] { package });
                }
            }
        }

        private void DrawOperationProgress()
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 18f);
            float progress;
            string label;

            if (_operationPhase == OperationPhase.Importing)
            {
                progress = _operationCount > 0
                    ? Mathf.Clamp01((float)(_operationIndex - 1) / _operationCount)
                    : 0f;
                label = $"Importing {_operationIndex} of {_operationCount}...";
            }
            else
            {
                progress = _downloadTotalBytes > 0L
                    ? Mathf.Clamp01((float)_downloadedBytes / _downloadTotalBytes)
                    : 0f;

                label = _downloadTotalBytes > 0L
                    ? $"{_downloadedBytes:N0} / {_downloadTotalBytes:N0} bytes"
                    : $"{_downloadedBytes:N0} bytes downloaded";

                if (!string.IsNullOrEmpty(_downloadLabel))
                {
                    label = _downloadLabel;
                }

                if (_operationCount > 1)
                {
                    label = $"{_operationIndex} of {_operationCount}  \u2022  {label}";
                }
            }

            EditorGUI.ProgressBar(progressRect, progress, label);
        }

        private static void DrawSeparator()
        {
            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1f);
            Color separatorColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.16f);
            EditorGUI.DrawRect(separatorRect, separatorColor);
        }

        private void DrawReleaseNotes(PackageEntry package)
        {
            Rect foldoutRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            bool expanded = EditorGUI.Foldout(foldoutRect, package.releaseNotesExpanded,
                "Release notes", true);

            if (expanded != package.releaseNotesExpanded)
            {
                package.releaseNotesExpanded = expanded;
                package.releaseNotesInitialized = true;
            }

            if (!package.releaseNotesExpanded) return;

            string[] releaseNotes = GetReleaseNotes(package)
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Select(note => note.Trim())
                .ToArray();

            EditorGUILayout.Space(2f);
            if (releaseNotes.Length == 0)
            {
                EditorGUILayout.LabelField("No release notes available.", _releaseNotesStyle);
                return;
            }

            string prefix = releaseNotes.Length > 1 ? "\u2022 " : string.Empty;
            foreach (string releaseNote in releaseNotes)
            {
                EditorGUILayout.LabelField($"{prefix}{releaseNote}", _releaseNotesStyle);
            }
        }

        private static string[] GetReleaseNotes(PackageEntry package)
        {
            bool useRemote = package.remoteState == RemoteState.Ready &&
                             (package.versionRelation == VersionRelation.RemoteNewer ||
                              package.versionRelation == VersionRelation.Equal);

            return useRemote
                ? package.remoteManifest?.changelog ?? Array.Empty<string>()
                : package.localManifest?.changelog ?? Array.Empty<string>();
        }

        private async void StartOperation(IEnumerable<PackageEntry> requestedPackages)
        {
            if (_refreshing || IsOperationActive) return;

            var packages = requestedPackages
                .Where(package => package.remoteState == RemoteState.Ready &&
                                  (package.versionRelation == VersionRelation.RemoteNewer ||
                                   package.versionRelation == VersionRelation.Equal))
                .Distinct()
                .ToList();

            if (packages.Count == 0) return;

            foreach (var package in packages)
            {
                package.operationMessage = null;
            }

            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _operationCancellation.Token;

            _operationPhase = OperationPhase.Downloading;
            _operationCount = packages.Count;
            var downloadedPackages = new List<DownloadedPackage>();
            bool importedAny = false;
            bool reloadLocked = false;

            try
            {
                for (int index = 0; index < packages.Count; index++)
                {
                    PackageEntry package = packages[index];
                    _operationEntry = package;
                    _operationIndex = index + 1;
                    Repaint();

                    try
                    {
                        string packagePath = await DownloadPackage(package, cancellationToken);
                        downloadedPackages.Add(new DownloadedPackage(package, packagePath));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        package.operationMessage = "Download cancelled.";
                        package.operationMessageType = MessageType.Warning;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        package.operationMessage = "The package download timed out.";
                        package.operationMessageType = MessageType.Error;
                        Debug.LogError($"Failed to download '{package.DisplayName}': request timed out.");
                    }
                    catch (Exception exception)
                    {
                        package.operationMessage = $"Download failed: {exception.Message}";
                        package.operationMessageType = MessageType.Error;
                        Debug.LogError($"Failed to download '{package.DisplayName}': {exception.Message}");
                    }
                }

                if (downloadedPackages.Count == 0) return;

                _operationPhase = OperationPhase.Importing;
                _operationCount = downloadedPackages.Count;
                _downloadLabel = null;
                EditorApplication.LockReloadAssemblies();
                reloadLocked = true;

                for (int index = 0; index < downloadedPackages.Count; index++)
                {
                    DownloadedPackage downloadedPackage = downloadedPackages[index];
                    _operationEntry = downloadedPackage.package;
                    _operationIndex = index + 1;
                    Repaint();

                    ImportResult result = await ImportPackage(downloadedPackage.packagePath);
                    if (result.succeeded)
                    {
                        importedAny = true;
                        downloadedPackage.package.operationMessage = null;
                        continue;
                    }

                    downloadedPackage.package.operationMessage = result.error;
                    downloadedPackage.package.operationMessageType = MessageType.Error;
                    Debug.LogError($"Failed to import '{downloadedPackage.package.DisplayName}': {result.error}");
                }
            }
            catch (Exception exception)
            {
                if (_operationEntry != null)
                {
                    _operationEntry.operationMessage = $"Package operation failed: {exception.Message}";
                    _operationEntry.operationMessageType = MessageType.Error;
                }
                Debug.LogException(exception);
            }
            finally
            {
                _operationEntry = null;
                _operationPhase = OperationPhase.None;
                _operationIndex = 0;
                _operationCount = 0;
                _downloadLabel = null;
                _operationCancellation?.Dispose();
                _operationCancellation = null;

                if (reloadLocked)
                {
                    EditorApplication.UnlockReloadAssemblies();
                }

                Repaint();
            }

            if (importedAny)
            {
                Refresh();
            }
        }

        private async Task<string> DownloadPackage(PackageEntry package, CancellationToken cancellationToken)
        {
            _downloadedBytes = 0L;
            _downloadTotalBytes = 0L;
            _downloadLabel = "Starting download...";

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDirectory = Path.Combine(projectRoot, DownloadDirectory);
            string fileName = MakeSafeFileName(
                $"{package.localManifest.packageId}_{package.remoteManifest.version}.unitypackage");
            string packagePath = Path.Combine(outputDirectory, fileName);
            string partialPath = packagePath + ".partial";

            try
            {
                Directory.CreateDirectory(outputDirectory);

                using (HttpResponseMessage response = await HttpClient.GetAsync(
                           package.remoteManifest.downloadUrl,
                           HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"Download returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
                    }

                    _downloadTotalBytes = response.Content.Headers.ContentLength ?? 0L;
                    _downloadLabel = null;

                    using (Stream input = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(
                               partialPath,
                               FileMode.Create,
                               FileAccess.Write,
                               FileShare.None,
                               81920,
                               true))
                    {
                        var buffer = new byte[81920];
                        int read;

                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellationToken);
                            _downloadedBytes += read;
                            Repaint();
                        }
                    }

                    if (_downloadTotalBytes > 0L && _downloadedBytes != _downloadTotalBytes)
                    {
                        throw new EndOfStreamException(
                            $"Expected {_downloadTotalBytes:N0} bytes but received {_downloadedBytes:N0}.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(packagePath))
                {
                    File.Replace(partialPath, packagePath, null);
                }
                else
                {
                    File.Move(partialPath, packagePath);
                }

                return packagePath;
            }
            catch
            {
                TryDelete(partialPath);
                throw;
            }
        }

        private static async Task<ImportResult> ImportPackage(string packagePath)
        {
            var completion = new TaskCompletionSource<ImportResult>();

            void OnCompleted(string packageName)
            {
                completion.TrySetResult(new ImportResult(true, null));
            }

            void OnFailed(string packageName, string errorMessage)
            {
                completion.TrySetResult(new ImportResult(false, errorMessage));
            }

            void OnCancelled(string packageName)
            {
                completion.TrySetResult(new ImportResult(false, "Package import was cancelled."));
            }

            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.importPackageCancelled += OnCancelled;

            try
            {
                AssetDatabase.ImportPackage(packagePath, false);

                Task completedTask = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(2)));
                return completedTask == completion.Task
                    ? await completion.Task
                    : new ImportResult(false, "Package import did not complete within two minutes.");
            }
            catch (Exception exception)
            {
                return new ImportResult(false, exception.Message);
            }
            finally
            {
                AssetDatabase.importPackageCompleted -= OnCompleted;
                AssetDatabase.importPackageFailed -= OnFailed;
                AssetDatabase.importPackageCancelled -= OnCancelled;
            }
        }

        private static bool TryParseLocalManifest(string json, out LocalManifest manifest,
            out SemanticVersion version, out string error)
        {
            manifest = null;
            version = default;

            try
            {
                manifest = JsonUtility.FromJson<LocalManifest>(json);
            }
            catch (Exception exception)
            {
                error = $"Malformed local manifest JSON: {exception.Message}";
                return false;
            }

            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.packageId) ||
                string.IsNullOrWhiteSpace(manifest.displayName) ||
                string.IsNullOrWhiteSpace(manifest.version) ||
                string.IsNullOrWhiteSpace(manifest.manifestUrl))
            {
                error = "The local manifest must define packageId, displayName, version, and manifestUrl.";
                return false;
            }

            manifest.packageId = manifest.packageId.Trim();
            manifest.displayName = manifest.displayName.Trim();
            manifest.version = manifest.version.Trim();
            manifest.manifestUrl = manifest.manifestUrl.Trim();
            manifest.changelog = manifest.changelog ?? Array.Empty<string>();

            if (!SemanticVersion.TryParse(manifest.version, out version))
            {
                error = $"Local version '{manifest.version}' must use numeric major.minor.patch format.";
                return false;
            }

            if (!TryGetHttpsUri(manifest.manifestUrl, out _))
            {
                error = "The local manifestUrl must be an absolute HTTPS URL.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryParseRemoteManifest(string json, string expectedPackageId,
            out RemoteManifest manifest, out SemanticVersion version, out string error)
        {
            manifest = null;
            version = default;

            try
            {
                manifest = JsonUtility.FromJson<RemoteManifest>(json);
            }
            catch (Exception exception)
            {
                error = $"Malformed remote manifest JSON: {exception.Message}";
                return false;
            }

            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.packageId) ||
                string.IsNullOrWhiteSpace(manifest.version) ||
                string.IsNullOrWhiteSpace(manifest.downloadUrl) ||
                manifest.changelog == null)
            {
                error = "The remote manifest must define packageId, version, downloadUrl, and changelog.";
                return false;
            }

            manifest.packageId = manifest.packageId.Trim();
            manifest.version = manifest.version.Trim();
            manifest.downloadUrl = manifest.downloadUrl.Trim();

            if (!string.Equals(manifest.packageId, expectedPackageId, StringComparison.Ordinal))
            {
                error =
                    $"Remote package ID '{manifest.packageId}' does not match local package ID '{expectedPackageId}'.";
                return false;
            }

            if (!SemanticVersion.TryParse(manifest.version, out version))
            {
                error = $"Remote version '{manifest.version}' must use numeric major.minor.patch format.";
                return false;
            }

            if (!TryGetHttpsUri(manifest.downloadUrl, out _))
            {
                error = "The remote downloadUrl must be an absolute HTTPS URL.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryGetHttpsUri(string value, out Uri uri)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToAssetPath(string absolutePath)
        {
            string relativePath = absolutePath.Substring(Application.dataPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            return string.IsNullOrEmpty(relativePath) ? "Assets" : $"Assets/{relativePath}";
        }

        private static string MakeSafeFileName(string fileName)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            return new string(fileName
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray());
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not remove partial package download '{path}': {exception.Message}");
            }
        }

        private static void Repaint()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        [Serializable]
        private sealed class LocalManifest
        {
            public string packageId;
            public string displayName;
            public string version;
            public string manifestUrl;
            public string[] changelog;
        }

        [Serializable]
        private sealed class RemoteManifest
        {
            public string packageId;
            public string version;
            public string downloadUrl;
            public string[] changelog;
        }

        private sealed class PackageEntry
        {
            public string manifestPath;
            public string packageRoot;
            public string packageRootAssetPath;
            public string fallbackName;
            public LocalManifest localManifest;
            public RemoteManifest remoteManifest;
            public SemanticVersion localVersion;
            public SemanticVersion remoteVersion;
            public RemoteState remoteState;
            public VersionRelation versionRelation;
            public string status;
            public PackageStatusKind statusKind;
            public string operationMessage;
            public MessageType operationMessageType;
            public bool releaseNotesExpanded;
            public bool releaseNotesInitialized;

            public string DisplayName => localManifest?.displayName ?? fallbackName;
        }

        private sealed class DownloadedPackage
        {
            public readonly PackageEntry package;
            public readonly string packagePath;

            public DownloadedPackage(PackageEntry package, string packagePath)
            {
                this.package = package;
                this.packagePath = packagePath;
            }
        }

        private readonly struct ImportResult
        {
            public readonly bool succeeded;
            public readonly string error;

            public ImportResult(bool succeeded, string error)
            {
                this.succeeded = succeeded;
                this.error = error;
            }
        }

        private readonly struct SemanticVersion : IComparable<SemanticVersion>
        {
            private readonly int _major;
            private readonly int _minor;
            private readonly int _patch;

            private SemanticVersion(int major, int minor, int patch)
            {
                _major = major;
                _minor = minor;
                _patch = patch;
            }

            public int CompareTo(SemanticVersion other)
            {
                int comparison = _major.CompareTo(other._major);
                if (comparison != 0) return comparison;

                comparison = _minor.CompareTo(other._minor);
                return comparison != 0 ? comparison : _patch.CompareTo(other._patch);
            }

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = default;
                if (string.IsNullOrEmpty(value)) return false;

                string[] parts = value.Split('.');
                if (parts.Length != 3 ||
                    !TryParsePart(parts[0], out int major) ||
                    !TryParsePart(parts[1], out int minor) ||
                    !TryParsePart(parts[2], out int patch))
                {
                    return false;
                }

                version = new SemanticVersion(major, minor, patch);
                return true;
            }

            private static bool TryParsePart(string value, out int result)
            {
                result = 0;
                return !string.IsNullOrEmpty(value) &&
                       value.All(character => character >= '0' && character <= '9') &&
                       int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
            }
        }

        private enum RemoteState
        {
            Pending,
            Ready,
            Invalid
        }

        private enum VersionRelation
        {
            Unknown,
            RemoteOlder,
            Equal,
            RemoteNewer
        }

        private enum PackageStatusKind
        {
            Checking,
            UpdateAvailable,
            UpToDate,
            Warning,
            Error
        }

        private enum OperationPhase
        {
            None,
            Downloading,
            Importing
        }
    }
}
