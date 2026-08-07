using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.Gallery
{
    /// <summary>
    /// Shows what <see cref="GalleryService"/> holds: a grid of thumbnails with their ratings (Story 1.11,
    /// AC2). Data and view are kept apart — the service owns the shots and knows nothing about UI; this
    /// draws them and stores nothing.
    ///
    /// ⚠️ THIS CANVAS IS SCREEN SPACE – CAMERA, UNLIKE THE VIEWFINDER, AND THAT IS A DELIBERATE DEVIATION.
    /// An Overlay canvas is composited after every camera and therefore cannot be captured by
    /// <c>cam.Render()</c> — which would make an Overlay gallery unphotographable by any rig, leaving AC2
    /// provable only by asking a human to look at the screen. Rendering through the camera makes the
    /// gallery visible to the same verification path that proved Stories 1.9 and 1.10. It is safe because
    /// the gallery cannot be open while a capture happens (see <see cref="Toggle"/>), so it can never leak
    /// into a stored photograph.
    ///
    /// Cells are built ONCE at Awake and enabled/disabled thereafter — never Instantiate/Destroy per open
    /// (architecture §Consistency Rules). The pool is sized from the service's own capacity, so it cannot
    /// drift from the number of shots that can exist.
    ///
    /// Art direction is deliberately plain. The GDD wants UI surfaces to read like a 2000s camcorder
    /// (gdd.md:322) and epics.md marks that polish-acceptable on Story 1.12's HUD; a legible grid is the
    /// right amount of work here — enough not to be thrown away, not so much as to be styling on spec.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(CanvasGroup))]
    public class GalleryView : MonoBehaviour
    {
        [Header("Wiring")]

        [Tooltip("The service holding the shots. If unassigned, the gallery opens empty rather than not at " +
                 "all — an empty gallery with a clean console is a legible state; a dead key is not.")]
        [SerializeField] private GalleryService service;

        [Tooltip("The camera this canvas renders through (Screen Space - Camera). MUST be the same camera " +
                 "PhotoModeController uses, or the gallery will draw into a view nobody is looking at. If " +
                 "unassigned, falls back to Camera.main at Awake.")]
        [SerializeField] private Camera uiCamera;

        [Tooltip("The controller whose photo mode gates this gallery: it opens only in Walk, and while it " +
                 "is open the camera cannot be raised (so capture and zoom stay inert too). If unassigned, " +
                 "the gallery still opens and closes but stops gating the camera.")]
        [SerializeField] private PhotoModeController photoMode;

        [Tooltip("The player, frozen while the gallery is open. The backdrop is fully opaque, so without " +
                 "this the player walks blind behind their own photographs — the 2026-07-30 review found " +
                 "they could stroll off a ledge. If unassigned, the gallery still opens but stops freezing " +
                 "movement.")]
        [SerializeField] private ThirdPersonController player;

        [Tooltip("Font for the rating line under each thumbnail. Optional — falls back to Unity's built-in " +
                 "LegacyRuntime font, which is a built-in ENGINE resource and not a project asset, so this " +
                 "is not the Resources.Load the architecture rules out for game data.")]
        [SerializeField] private Font cellFont;

        [Header("Layout")]

        [Tooltip("LARGEST a thumbnail cell may be drawn. Cells shrink below this to fit however many shots " +
                 "the player is actually holding — see LayoutGrid. They are never drawn bigger, so three " +
                 "shots do not become three billboards.")]
        [SerializeField] private Vector2 cellSize = new Vector2(240f, 135f);

        [Tooltip("SMALLEST a thumbnail cell may be drawn. Below this a photograph stops being readable as " +
                 "a photograph, so the gallery shows fewer of them rather than shrinking further — and " +
                 "says so in the header.")]
        [SerializeField] private Vector2 minCellSize = new Vector2(96f, 54f);

        [Tooltip("Pixel gap between cells, and the margin around the whole grid.")]
        [SerializeField] private float spacing = 12f;

        [Tooltip("Point size of the rating line under each thumbnail.")]
        [SerializeField, Range(8, 48)] private int fontSize = 16;

        /// <summary>Sorting order for the gallery canvas. High so it draws over anything else this camera
        /// renders; it is still BELOW the Overlay canvases (viewfinder, flash), which are composited after
        /// all cameras regardless of sorting order — harmless, because neither is visible in Walk mode.</summary>
        private const int GallerySortingOrder = 500;

        /// <summary>
        /// Backdrop behind the grid. FULLY OPAQUE, and that is a correctness decision rather than a taste one.
        ///
        /// ⚠️ PARTIAL ALPHA DOES NOT WORK IN THIS PROJECT, and the rig's private world cannot show you why.
        /// A Screen Space – Camera canvas is composited in the TRANSPARENT pass, i.e. BEFORE URP's
        /// post-processing — so whatever leaks through the backdrop is then run through the Global Volume's
        /// tonemapping along with the scene. The town renders bright HDR values (open sky, sunlit ground),
        /// so even a 1.5% leak at alpha 0.985 came back as a clearly readable forest of trees across the
        /// bottom half of the gallery (real-scene check, 2026-07-30). Measured in the same run: at alpha 1
        /// the same pixels sample exactly (0,0,0).
        ///
        /// The rig's world is a grey plane with no Volume and no bright sky, so it looked perfect there at
        /// 0.92 AND at 0.985. This is the one defect in this story that only the real scene could produce.
        /// </summary>
        private static readonly Color BackdropColor = new Color(0.05f, 0.05f, 0.07f, 1f);

        private static readonly Color CountedTextColor = new Color(0.94f, 0.94f, 0.90f);
        private static readonly Color MissTextColor = new Color(1f, 0.55f, 0.45f);
        private static readonly Color EmptyImageColor = new Color(0.18f, 0.18f, 0.22f);

        private Canvas _canvas;
        private CanvasGroup _group;

        // One entry per cell, built once at Awake. Parallel handles rather than a component per cell: there
        // is nothing per-cell to update every frame, so a MonoBehaviour each would be three hundred bytes
        // and an extra script lifecycle for no behaviour.
        private readonly List<RawImage> _cellImages = new List<RawImage>();
        private readonly List<Text> _cellLabels = new List<Text>();
        private readonly List<GameObject> _cells = new List<GameObject>();

        private Text _headerLabel;

        // Kept so Refresh can re-fit the grid to however many shots exist. Sizing the grid ONCE at Awake is
        // what let the first run push the newest row off the bottom of the screen.
        private GridLayoutGroup _grid;
        private RectTransform _gridRect;

        private bool _viewReady;

        /// <summary>True while the gallery is on screen.</summary>
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _group = GetComponent<CanvasGroup>();

            if (uiCamera == null) uiCamera = Camera.main;   // cached once; never Camera.main in a frame path

            if (uiCamera == null)
            {
                // Without a camera a Screen Space - Camera canvas renders nothing at all, silently. Say so
                // once and stand down rather than presenting a key that appears to do nothing.
                GameLog.Error("Gallery",
                    "No camera for the gallery canvas — the gallery cannot be shown. Everything else " +
                    "(capture, grading, storing shots) is unaffected.", this);
                _viewReady = false;

                // ⚠️ HIDE ON THE WAY OUT. The scene ships this canvas ENABLED at alpha 1, and Close() early-
                // returns on !_viewReady — so returning here without hiding leaves it on with nothing able to
                // turn it off, which is the opposite of AC5's "disables only its own function". Found by the
                // 2026-07-30 review.
                ApplyOpenState(false);
                return;
            }

            if (service == null)
                GameLog.Error("Gallery",
                    "GalleryView has no GalleryService — the gallery will open empty.", this);

            ValidateLayout();

            ConfigureCanvas();
            BuildCells();

            _viewReady = true;
            ApplyOpenState(false);
        }

        /// <summary>
        /// Repairs unusable layout tunables and says so, once, at Awake.
        ///
        /// ⚠️ THESE FOUR FIELDS HAD NO VALIDATION AT ALL while GalleryConfig validates hard, and the gap was
        /// reachable: <c>cellSize.x = 0</c> is clamped DOWN by <see cref="LayoutGrid"/> but never back up to
        /// <see cref="minCellSize"/>, so every cell was activated at zero width and the gallery opened to a
        /// backdrop and a header reading "50 shots" with no photographs on it and a clean console — the same
        /// silent-nothing class this project has now been caught by five times. Found by the 2026-07-30 review.
        ///
        /// ⚠️ DELIBERATELY NOT AN <c>OnValidate</c>. That is the trap this same review found in GalleryConfig:
        /// OnValidate runs on asset load, BEFORE Awake, so it repairs the value and the warning that was
        /// supposed to report it can then never fire. Clamping here, where the warning is issued, means the
        /// authored value is still intact at the moment it is described.
        ///
        /// Reports EVERY problem in one message rather than the first — a designer with three mistakes
        /// should not need three play-mode cycles to find them (the first-wins complaint standing against
        /// both config validators).
        /// </summary>
        private void ValidateLayout()
        {
            var problems = new List<string>();

            if (spacing < 0f)
            {
                problems.Add($"spacing is {spacing}, which overlaps cells — using 0.");
                spacing = 0f;
            }

            if (fontSize < MinFontSize)
            {
                problems.Add($"fontSize is {fontSize}, below the {MinFontSize} the captions floor at — using " +
                             $"{MinFontSize}, and compact captions can never engage.");
                fontSize = MinFontSize;
            }

            // The floor first: cellSize is validated against it below, so a broken floor would pass a broken
            // ceiling as sane.
            if (minCellSize.x < MinCellEdge || minCellSize.y < MinCellEdge)
            {
                Vector2 was = minCellSize;
                minCellSize = new Vector2(Mathf.Max(minCellSize.x, MinCellEdge),
                                          Mathf.Max(minCellSize.y, MinCellEdge));
                problems.Add($"minCellSize is {was}, at or below zero on an axis — a cell that size draws " +
                             $"nothing at all. Using {minCellSize}.");
            }

            if (cellSize.x < minCellSize.x || cellSize.y < minCellSize.y)
            {
                Vector2 was = cellSize;
                cellSize = new Vector2(Mathf.Max(cellSize.x, minCellSize.x),
                                       Mathf.Max(cellSize.y, minCellSize.y));
                problems.Add($"cellSize is {was}, smaller than minCellSize {minCellSize} — the largest a cell " +
                             $"may be drawn cannot be below the smallest, and at zero the gallery opens with " +
                             $"no visible photographs and a clean console. Using {cellSize}.");
            }

            if (problems.Count > 0)
                GameLog.Warn("Gallery", "GalleryView layout: " + string.Join("  ", problems.ToArray()));
        }

        /// <summary>Smallest a cell edge may be authored at. Below this a cell is not small, it is absent.</summary>
        private const float MinCellEdge = 8f;

        private void ConfigureCanvas()
        {
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = uiCamera;
            _canvas.sortingOrder = GallerySortingOrder;

            // ⚠️ Just past the near plane, NOT the default 100. A Screen Space - Camera canvas is drawn at
            // a real distance in the world and world geometry in front of it OCCLUDES it — and this game's
            // town is modelled at roughly 4x metric scale, so a canvas 100 units out would spend most of its
            // life inside a building. Sitting it a hair past the near plane means nothing can get in front
            // of it without also being inside the lens.
            _canvas.planeDistance = uiCamera.nearClipPlane + 0.1f;

            // ⚠️ A camera that does not render this canvas's LAYER draws exactly nothing, with a clean
            // console and a key that appears to work. This project has been caught by silent-nothing four
            // times (cueRadius = 0, minCoverage = NaN, minVisibleSamples = 0, timingFullSeconds = 0), and
            // "the gallery opens and the screen does not change" is the same bug wearing a different hat —
            // one that would be diagnosed as a UI layout problem for an hour before anyone checked a mask.
            if ((uiCamera.cullingMask & (1 << gameObject.layer)) == 0)
                GameLog.Warn("Gallery",
                    $"the gallery canvas is on layer '{LayerMask.LayerToName(gameObject.layer)}' " +
                    $"({gameObject.layer}), which camera '{uiCamera.name}' does not render — the gallery " +
                    "will open and show nothing at all. Put the canvas on a layer inside the camera's " +
                    "Culling Mask, or add that layer to the mask.");
        }

        /// <summary>
        /// Builds the backdrop, the header and one cell per slot the service can hold — once, at Awake.
        /// Sized from <see cref="GalleryService.Capacity"/> so the pool and the store cannot disagree; a
        /// service that is not recording reports 0 and the gallery is simply an empty frame.
        /// </summary>
        private void BuildCells()
        {
            int capacity = service != null ? service.Capacity : 0;

            Font font = cellFont != null ? cellFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                // Not fatal: the thumbnails still draw, only the rating line is missing. Said once so a
                // gallery with unlabelled pictures is explained rather than mysterious.
                GameLog.Warn("Gallery",
                    "No font available for the gallery labels — thumbnails will show without their ratings.");

            var backdrop = NewUIObject("Backdrop", transform);
            Stretch(backdrop.GetComponent<RectTransform>());
            var backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = BackdropColor;
            backdropImage.raycastTarget = false;

            var header = NewUIObject("Header", transform);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(spacing * 2f, -(fontSize * 2.2f));
            headerRect.offsetMax = new Vector2(-spacing * 2f, -spacing);
            _headerLabel = MakeText(header, font, Mathf.RoundToInt(fontSize * 1.25f), TextAnchor.MiddleLeft, wrap: false);

            var grid = NewUIObject("Grid", transform);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            Stretch(gridRect);
            gridRect.offsetMin = new Vector2(spacing * 2f, spacing * 2f);
            gridRect.offsetMax = new Vector2(-spacing * 2f, -(fontSize * 2.6f + spacing));

            _gridRect = gridRect;
            _grid = grid.AddComponent<GridLayoutGroup>();
            _grid.cellSize = new Vector2(cellSize.x, cellSize.y + LabelHeight);
            _grid.spacing = new Vector2(spacing, spacing);
            _grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            _grid.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < capacity; i++) BuildCell(grid.transform, font);
        }

        private void BuildCell(Transform parent, Font font)
        {
            var cell = NewUIObject($"Cell{_cells.Count:D3}", parent);

            var picture = NewUIObject("Picture", cell.transform);
            RectTransform pictureRect = picture.GetComponent<RectTransform>();

            // STRETCHED to the cell, not a fixed height. The cell's size is decided per-open by
            // LayoutGrid, so a picture pinned to a fixed offset would keep its Awake-time size while the
            // cell around it shrank — cells overlapping their own labels.
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = new Vector2(0f, LabelHeight);
            pictureRect.offsetMax = Vector2.zero;

            // RawImage, not Image: it takes a Texture2D directly, where Image would need a Sprite — an extra
            // allocation per thumbnail, per open, for no benefit.
            var raw = picture.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var label = NewUIObject("Label", cell.transform);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(0f, LabelHeight);

            _cells.Add(cell);
            _cellImages.Add(raw);
            _cellLabels.Add(MakeText(label, font, fontSize, TextAnchor.UpperLeft, wrap: true));

            cell.SetActive(false);
        }

        private static Text MakeText(GameObject host, Font font, int size, TextAnchor anchor, bool wrap)
        {
            var text = host.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = CountedTextColor;
            text.raycastTarget = false;

            // WRAP for cell captions, Overflow for the header. The header sits alone on a full-width row
            // and has nothing to collide with; a cell caption has seven neighbours, and Overflow let it
            // print straight across them.
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static GameObject NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            // Inherit the layer explicitly. SetParent does NOT do this, so every cell would otherwise be
            // born on layer 0 regardless of where the canvas sits — which means a gallery correctly placed
            // on the UI layer would draw its backdrop and its cells through different culling masks, and
            // could show a backdrop with no photographs on it. Same class of half-invisible failure the
            // culling-mask check in ConfigureCanvas guards.
            go.layer = parent.gameObject.layer;
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // =====================================================================
        // OPEN / CLOSE
        // =====================================================================

        /// <summary>
        /// Opens the gallery, or closes it if it is already open. The input adapter on the player object is
        /// a thin wrapper over this, so the rig can drive the real path without synthesising an InputValue —
        /// the same shape that made Stories 1.9 and 1.10 provable.
        ///
        /// ⚠️ THE GALLERY OPENS ONLY IN WALK MODE (AC2). Refused rather than queued while the camera is
        /// raised: a gallery that pops open the moment you lower the camera would be a surprise, and a
        /// player mid-shot pressing Tab meant "not now".
        /// </summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Opens the gallery and refreshes it from the service. No-op while the camera is raised.</summary>
        public void Open()
        {
            if (!_viewReady || IsOpen) return;
            if (photoMode != null && photoMode.IsPhotoMode) return;   // AC2: Walk mode only

            // ⚠️ SHOW FIRST, FILL SECOND. Refresh sizes the grid from the RectTransform's measured rect, and
            // a DISABLED canvas never lays out — so refreshing before enabling read a zero-sized rect, and
            // LayoutGrid bailed out leaving the Awake cell size in place. It only bit the FIRST open (later
            // ones inherited a rect from the previous layout pass), which is exactly the kind of bug that
            // survives a bench that opens the gallery more than once and never looks at the first picture.
            ApplyOpenState(true);
            Canvas.ForceUpdateCanvases();
            Refresh();

            // While the gallery owns the screen the camera cannot be raised — which makes capture and zoom
            // inert too, because both already gate on IsPhotoMode. No new flags in PhotoModeController and
            // no new way to photograph the gallery UI.
            if (photoMode != null) photoMode.SetRaiseSuppressed(true);

            // ...and the player stops walking. The backdrop is fully opaque, so anything that still moves
            // behind it moves blind (2026-07-30 review).
            if (player != null) player.SetInputSuppressed(true);
        }

        /// <summary>Closes the gallery and hands the camera and the player back.</summary>
        public void Close()
        {
            if (!_viewReady || !IsOpen) return;

            ApplyOpenState(false);
            ReleaseSuppression();
        }

        /// <summary>
        /// Hands back everything <see cref="Open"/> took. Split out of <see cref="Close"/> so the teardown
        /// path below can call it without needing the view to still be in a closable state.
        /// </summary>
        private void ReleaseSuppression()
        {
            if (photoMode != null) photoMode.SetRaiseSuppressed(false);
            if (player != null) player.SetInputSuppressed(false);
        }

        /// <summary>
        /// ⚠️ THE GALLERY MUST HAND THE CAMERA BACK EVEN WHEN IT IS NOT CLOSED POLITELY.
        ///
        /// <see cref="Open"/> asserts suppression on PhotoModeController and the player; before the
        /// 2026-07-30 review <see cref="Close"/> was the ONLY thing that released it, and this class had no
        /// teardown hook at all. Deactivate this GameObject while the gallery is open — a pause menu, an
        /// additive scene unload, tooling swapping the canvas, or the verification rig rebuilding the view
        /// between scenarios — and <c>RaiseSuppressed</c> stayed true for the rest of the session:
        /// <c>SetPhotoMode(true)</c> returns immediately, so the camera could never be raised again, no
        /// capture, no zoom, no grading, and a completely clean console.
        ///
        /// The rig already worked around this in its own code (GalleryShootRunner "close BEFORE destroying"),
        /// which is exactly the wrong place for the fix to live — the shipped scene got no such protection.
        ///
        /// ⚠️ GUARDED ON <see cref="IsOpen"/>, NOT UNCONDITIONAL. A closed gallery holds no suppression, so
        /// releasing anyway would clear a flag some OTHER owner had set — turning this fix into the very
        /// bug it exists to prevent, one caller along. (That the flag is a plain bool and cannot express
        /// two owners at once is recorded as deferred work against Story 1.12.)
        /// </summary>
        private void OnDisable()
        {
            if (!IsOpen) return;

            IsOpen = false;
            ReleaseSuppression();
        }

        /// <summary>
        /// Shows or hides the canvas.
        ///
        /// Both the alpha AND <c>Canvas.enabled</c> are driven, deliberately. Alpha alone matches the
        /// viewfinder's precedent (PhotoModeController.cs:195-206), but this canvas renders THROUGH the
        /// camera, so a closed-but-enabled gallery is still geometry submitted on every frame — including
        /// the frame a photograph is taken on. Disabling the Canvas stops it rendering outright while
        /// leaving the hierarchy (and the cell pool) intact, which is the guarantee AC2's "no new way to
        /// photograph the gallery UI" actually needs.
        /// </summary>
        private void ApplyOpenState(bool open)
        {
            IsOpen = open;
            _canvas.enabled = open;
            _group.alpha = open ? 1f : 0f;
            _group.blocksRaycasts = open;
            _group.interactable = open;
        }

        /// <summary>
        /// Re-fits the grid when the canvas changes size — a window resize, a fullscreen toggle, a
        /// resolution change — but only while the gallery is actually on screen.
        ///
        /// ⚠️ WITHOUT THIS THE GRID IS SIZED ONCE PER OPEN AND NEVER AGAIN. <see cref="Refresh"/> is called
        /// only from <see cref="Open"/>, and the cell size it computes is in PIXELS against a Constant Pixel
        /// Size scaler — so resizing the window mid-open left cells laid out for the old resolution, spilling
        /// off the bottom and right edges (the exact regression <see cref="LayoutGrid"/> was written to fix)
        /// while the header went on reporting a count measured against a rect that no longer existed.
        /// Closing and reopening fixed it, and nothing told the player that. Found by the 2026-07-30 review.
        ///
        /// Unity calls this on the frame the rect changes, not per frame, so it is not a per-frame path.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            // Fires during Awake before the cells exist, hence the readiness guard as well as IsOpen.
            if (!_viewReady || !IsOpen) return;

            Refresh();
        }

        /// <summary>
        /// Refills the cells from the service. Called on open rather than on every capture: the gallery
        /// cannot be open while a shot is taken, so there is no state to miss, and nothing here runs in a
        /// per-frame path.
        /// </summary>
        public void Refresh()
        {
            if (!_viewReady) return;

            IReadOnlyList<CapturedShot> shots = service != null ? service.Shots : null;
            int count = shots != null ? shots.Count : 0;
            int slots = _cells.Count;

            // Fit the grid to what is actually being shown BEFORE filling it, and find out how many of them
            // will genuinely be on screen.
            int shown = LayoutGrid(count, ShownAspect(shots, count));

            if (_headerLabel != null)
            {
                if (count == 0)
                    _headerLabel.text = "GALLERY — no shots yet";
                else if (shown < count)
                    // Say it out loud. A gallery quietly showing 6 of 50 is indistinguishable from a
                    // gallery that lost 44 photographs, and that is the worse of the two to be wrong about.
                    _headerLabel.text = $"GALLERY — {count} shots · showing newest {shown}";
                else
                    _headerLabel.text = $"GALLERY — {count} shot{(count == 1 ? "" : "s")}" +
                                        (slots > 0 ? $" (holds {slots})" : string.Empty);
            }

            int capFont = FontFor(_cellWidth);
            float band = LabelHeightFor(capFont);

            // When the cells get small the subject's name is the first thing to go: the stars and the
            // "why" are what AC2 is about, and a name that wraps to a third line pushes both out of view.
            bool compact = capFont < fontSize;

            for (int i = 0; i < slots; i++)
            {
                // Anything past what fits on screen is hidden rather than drawn off the edge — that is what
                // pushed the newest row off the bottom on the first run. `shown` is already clamped to the
                // shot count, so this covers empty slots too.
                if (i >= shown)
                {
                    _cells[i].SetActive(false);

                    // Drop the texture reference on a hidden cell as well. A RawImage holding an evicted
                    // Texture2D is a dangling native reference the moment eviction destroys it — Unity draws
                    // it as pink rather than crashing, but pink squares in a gallery are indistinguishable
                    // from a rendering bug, and pointing at destroyed memory is not a thing to leave lying
                    // around just because it happens to be survivable.
                    _cellImages[i].texture = null;
                    continue;
                }

                // NEWEST FIRST: the shot the player just took is the one they opened this to look at, and
                // the service stores oldest-first.
                CapturedShot shot = shots[count - 1 - i];
                _cells[i].SetActive(true);

                RawImage picture = _cellImages[i];
                picture.texture = shot.Image;

                // Re-fit the caption band to the size the grid just settled on. The cells are laid out per
                // open, so a band fixed at Awake would sit under a picture of a completely different size.
                picture.rectTransform.offsetMin = new Vector2(0f, band);

                // A shot stored without a picture (no camera) still gets a cell — a flat plate, so the entry
                // reads as "no picture" rather than as a black photograph.
                picture.color = shot.HasImage ? Color.white : EmptyImageColor;

                Text label = _cellLabels[i];
                if (label == null) continue;

                ((RectTransform)label.transform).offsetMax = new Vector2(0f, band);
                label.fontSize = capFont;

                label.text = DescribeShot(shot, compact);
                label.color = shot.Grade.IsMiss ? MissTextColor : CountedTextColor;
            }
        }

        /// <summary>
        /// The aspect the cells should be drawn at: the aspect of the pictures actually being shown.
        ///
        /// ⚠️ NOT <see cref="cellSize"/>'s ratio. Since the service derives a thumbnail's width from the
        /// game window, a hard-coded 16:9 cell would stretch every photograph taken in a window that is not
        /// 16:9 — the gallery would distort the very framing the derived width exists to preserve.
        ///
        /// Read from the NEWEST shot that has a picture. Shots taken before a mid-session window resize
        /// keep their own (now slightly different) aspect and will be a little stretched in their cell;
        /// that is a deliberate trade against giving every cell its own size, which would make the grid
        /// ragged for a case that only arises when someone drags the window mid-game.
        /// </summary>
        private float ShownAspect(IReadOnlyList<CapturedShot> shots, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                Texture2D img = shots[i].Image;
                if (img != null && img.height > 0) return img.width / (float)img.height;
            }

            return cellSize.x / Mathf.Max(1f, cellSize.y);   // nothing stored yet, or no pictures at all
        }

        /// <summary>Caption height at the DESIGN font size — the band reserved under a full-size cell.</summary>
        private float LabelHeight => LabelHeightFor(fontSize);

        /// <summary>Caption band for a given font size. Three lines' worth rather than two: the cell labels
        /// WRAP (see <see cref="MakeText"/>), so a long miss reason in a narrow cell needs somewhere to go
        /// that is not the cell next door.</summary>
        private static float LabelHeightFor(float font) => font * 3.6f;

        /// <summary>
        /// Caption font for a cell of a given width.
        ///
        /// ⚠️ THIS IS NOT COSMETIC. With a fixed font, a grid eight columns wide rendered
        /// "24 % · TownDrunk" wider than the cell holding it, and the text ran straight over the top of the
        /// neighbouring caption — producing "24 % · TownDrunk21 % · TownDrunk" across the whole row. Every
        /// rating on screen was unreadable while each individual cell was, technically, correct.
        /// </summary>
        private int FontFor(float cellWidth) =>
            Mathf.Clamp(Mathf.RoundToInt(cellWidth * 0.085f), MinFontSize, fontSize);

        /// <summary>Below this the caption is not worth drawing at all.</summary>
        private const int MinFontSize = 9;

        /// <summary>Width the grid settled on at the last <see cref="LayoutGrid"/>, so the captions can be
        /// scaled to match the cells they sit under.</summary>
        private float _cellWidth;

        /// <summary>
        /// Sizes the grid so the shots the player is holding actually fit on screen, and returns how many
        /// of them will be visible.
        ///
        /// ⚠️ WHY THIS EXISTS. The first version set <c>cellSize</c> once at Awake and let GridLayoutGroup
        /// flow as many rows as it liked. With eight shots the newest row was already sliced in half by the
        /// bottom of the screen, and at the shipped cap of fifty the player would have seen about six of
        /// them — with no indication the other forty-four existed. AC2 is "the player can open the gallery
        /// and SEE the shots"; a grid that silently runs off the screen fails it, and it fails it worse the
        /// more photographs you have taken, which is exactly backwards.
        ///
        /// Cells shrink to fit, down to <see cref="minCellSize"/> — below which a photograph stops reading
        /// as a photograph, so the gallery shows fewer of them and <see cref="Refresh"/> says so in the
        /// header instead of hiding the difference.
        /// </summary>
        private int LayoutGrid(int count, float aspect)
        {
            if (_grid == null || _gridRect == null || count <= 0) return 0;

            // The rect is not valid until the canvas has laid out at least once, and Open() calls Refresh()
            // on the same frame it enables the canvas — so force the layout rather than reading a zero and
            // computing a grid of infinite columns.
            Canvas.ForceUpdateCanvases();

            float w = _gridRect.rect.width;
            float h = _gridRect.rect.height;

            // Belt and braces for the zero-rect case above: derive the area from the canvas itself rather
            // than giving up and leaving cells at their Awake size, which is what clipped the first open.
            if (w <= 1f || h <= 1f)
            {
                Rect canvasRect = ((RectTransform)transform).rect;
                w = canvasRect.width  - spacing * 4f;
                h = canvasRect.height - spacing * 4f - fontSize * 2.6f;

                if (w <= 1f || h <= 1f)
                {
                    // Genuinely nothing to lay out into — a minimised window, a zero-size Game view, or a
                    // canvas that has not produced a rect yet.
                    //
                    // ⚠️ SET THE CELL SIZE ON THE WAY OUT, like the other two exits do. This branch used to
                    // `return count` and touch neither _cellWidth nor _grid.cellSize, so it claimed every
                    // shot was on screen while leaving cells at their Awake size and driving FontFor() from
                    // a stale (or, on a first open, zero) width — captions mis-banded against pictures of a
                    // different size, and the "showing newest N" warning could never fire. Found by the
                    // 2026-07-30 review. Return 0 rather than count: nothing is visible in a rect this size,
                    // and saying so is what keeps the header honest.
                    _cellWidth = minCellSize.x;
                    _grid.cellSize = new Vector2(minCellSize.x,
                                                 minCellSize.y + LabelHeightFor(FontFor(minCellSize.x)));
                    return 0;
                }
            }

            int bestCols = 0;
            float bestCellW = 0f;

            // Try every column count and keep the one that makes the cells LARGEST while still fitting every
            // shot. Iterating is fine: this runs once per open, over at most a few hundred entries.
            for (int cols = 1; cols <= count; cols++)
            {
                int rows = Mathf.CeilToInt(count / (float)cols);

                float cw = (w - spacing * (cols - 1)) / cols;
                float picH = cw / aspect;

                // The caption band shrinks with the cell, because the font does — otherwise a narrow cell
                // would reserve a full-size label under a postage-stamp picture.
                float ch = picH + LabelHeightFor(FontFor(cw));

                if (cw < minCellSize.x || picH < minCellSize.y) continue;
                if (rows * ch + spacing * (rows - 1) > h) continue;

                if (cw > bestCellW) { bestCellW = cw; bestCols = cols; }
            }

            if (bestCols > 0)
            {
                // Never bigger than the design size — three shots should not become three billboards.
                float cw = Mathf.Min(bestCellW, cellSize.x);
                _cellWidth = cw;
                _grid.cellSize = new Vector2(cw, cw / aspect + LabelHeightFor(FontFor(cw)));
                return count;
            }

            // Nothing fits at a readable size: fall back to the minimum cell and show as many of the NEWEST
            // as genuinely go on screen.
            float minW = minCellSize.x;
            float minPicH = minCellSize.y;
            float minLabel = LabelHeightFor(FontFor(minW));
            _cellWidth = minW;
            _grid.cellSize = new Vector2(minW, minPicH + minLabel);

            int fitCols = Mathf.Max(1, Mathf.FloorToInt((w + spacing) / (minW + spacing)));
            int fitRows = Mathf.Max(1, Mathf.FloorToInt((h + spacing) / (minPicH + minLabel + spacing)));
            return Mathf.Min(count, fitCols * fitRows);
        }

        /// <summary>
        /// The two lines under a thumbnail: the rating, and what the rating MEANS.
        ///
        /// ⚠️ STARS ALONE ARE NOT ENOUGH, AND THIS IS A KNOWN TRAP RATHER THAN A NICETY (AC2). Story 1.10's
        /// review settled that a shot taken well off the peak scores a hard 0% and therefore 1★ — identical
        /// to photographing an empty street. All 24 shots in the town placement study read
        /// "counted — 0% 1★". If the cell showed only stars, a MISSED shot and a merely LATE shot would be
        /// the same cell, and the player would have no way to tell "he was behind a wall" from "you were
        /// two seconds late". <see cref="ShotGrade.MissReason"/> is what separates them, so it is printed.
        /// </summary>
        private static string DescribeShot(CapturedShot shot, bool compact)
        {
            ShotGrade grade = shot.Grade;
            string who = shot.HasSubject ? shot.SubjectId : "nobody";

            if (grade.IsPlaceholder)
                return $"{GradeText.Stars(grade.Stars)}\nnot graded";

            // ⚠️ THE MISS REASON SURVIVES COMPACTION AND THE SUBJECT NAME DOES NOT. Telling a miss from a
            // merely late shot IS the acceptance criterion (both read 1★); naming who was in the frame is a
            // nicety. When space runs out, the nicety goes.
            // ⚠️ MissShort, NOT MissLong. The cell is narrow and gets narrower with every photograph taken;
            // the HUD's fuller sentences would truncate here, which is the exact regression the short set
            // was written against (GradeText.MissShort).
            if (grade.IsMiss)
                return $"{GradeText.Stars(grade.Stars)}\nmissed — {GradeText.MissShort(grade.MissReason)}";

            return compact
                ? $"{GradeText.Stars(grade.Stars)}\n{grade.Percent01:P0}"
                : $"{GradeText.Stars(grade.Stars)}\n{grade.Percent01:P0}  ·  {who}";
        }

        // The star glyphs and the miss vocabulary used to live here as two private statics. Story 1.12 moved
        // them to CameraGame.Grading.GradeText so the gallery and the grade-feedback HUD cannot drift into
        // describing the same photograph two different ways. The SHORT phrasings are byte-identical to what
        // shipped — they are load-bearing and were chosen against a truncation this grid actually produced.
    }
}
