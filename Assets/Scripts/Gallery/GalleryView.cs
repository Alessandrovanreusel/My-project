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

        [Tooltip("Font for the rating line under each thumbnail. Optional — falls back to Unity's built-in " +
                 "LegacyRuntime font, which is a built-in ENGINE resource and not a project asset, so this " +
                 "is not the Resources.Load the architecture rules out for game data.")]
        [SerializeField] private Font cellFont;

        [Header("Layout")]

        [Tooltip("Pixel size of one thumbnail cell in the grid. The picture keeps the config's 16:9 " +
                 "thumbnail aspect; this is how large it is drawn.")]
        [SerializeField] private Vector2 cellSize = new Vector2(240f, 135f);

        [Tooltip("Pixel gap between cells, and the margin around the whole grid.")]
        [SerializeField] private float spacing = 12f;

        [Tooltip("Point size of the rating line under each thumbnail.")]
        [SerializeField, Range(8, 48)] private int fontSize = 16;

        /// <summary>Sorting order for the gallery canvas. High so it draws over anything else this camera
        /// renders; it is still BELOW the Overlay canvases (viewfinder, flash), which are composited after
        /// all cameras regardless of sorting order — harmless, because neither is visible in Walk mode.</summary>
        private const int GallerySortingOrder = 500;

        /// <summary>Backdrop behind the grid, so thumbnails stay legible over a bright street.</summary>
        private static readonly Color BackdropColor = new Color(0.05f, 0.05f, 0.07f, 0.92f);

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
                return;
            }

            if (service == null)
                GameLog.Error("Gallery",
                    "GalleryView has no GalleryService — the gallery will open empty.", this);

            ConfigureCanvas();
            BuildCells();

            _viewReady = true;
            ApplyOpenState(false);
        }

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
            _headerLabel = MakeText(header, font, Mathf.RoundToInt(fontSize * 1.25f), TextAnchor.MiddleLeft);

            var grid = NewUIObject("Grid", transform);
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            Stretch(gridRect);
            gridRect.offsetMin = new Vector2(spacing * 2f, spacing * 2f);
            gridRect.offsetMax = new Vector2(-spacing * 2f, -(fontSize * 2.6f + spacing));

            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(cellSize.x, cellSize.y + fontSize * 2.4f);
            layout.spacing = new Vector2(spacing, spacing);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < capacity; i++) BuildCell(grid.transform, font);
        }

        private void BuildCell(Transform parent, Font font)
        {
            var cell = NewUIObject($"Cell{_cells.Count:D3}", parent);

            var picture = NewUIObject("Picture", cell.transform);
            RectTransform pictureRect = picture.GetComponent<RectTransform>();
            pictureRect.anchorMin = new Vector2(0f, 1f);
            pictureRect.anchorMax = new Vector2(1f, 1f);
            pictureRect.pivot = new Vector2(0.5f, 1f);
            pictureRect.offsetMin = new Vector2(0f, -cellSize.y);
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
            labelRect.offsetMax = new Vector2(0f, fontSize * 2.4f);

            _cells.Add(cell);
            _cellImages.Add(raw);
            _cellLabels.Add(MakeText(label, font, fontSize, TextAnchor.UpperLeft));

            cell.SetActive(false);
        }

        private static Text MakeText(GameObject host, Font font, int size, TextAnchor anchor)
        {
            var text = host.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = CountedTextColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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

            Refresh();
            ApplyOpenState(true);

            // While the gallery owns the screen the camera cannot be raised — which makes capture and zoom
            // inert too, because both already gate on IsPhotoMode. No new flags in PhotoModeController and
            // no new way to photograph the gallery UI.
            if (photoMode != null) photoMode.SetRaiseSuppressed(true);
        }

        /// <summary>Closes the gallery and hands the camera back.</summary>
        public void Close()
        {
            if (!_viewReady || !IsOpen) return;

            ApplyOpenState(false);
            if (photoMode != null) photoMode.SetRaiseSuppressed(false);
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

            if (_headerLabel != null)
                _headerLabel.text = count == 0
                    ? "GALLERY — no shots yet"
                    : $"GALLERY — {count} shot{(count == 1 ? "" : "s")}" +
                      (slots > 0 ? $" (holds {slots})" : string.Empty);

            for (int i = 0; i < slots; i++)
            {
                // NEWEST FIRST: the shot the player just took is the one they opened this to look at, and
                // the service stores oldest-first.
                int shotIndex = count - 1 - i;

                if (shotIndex < 0)
                {
                    _cells[i].SetActive(false);
                    // Drop the texture reference on a hidden cell as well. A RawImage holding an evicted
                    // Texture2D is a dangling native reference the moment eviction destroys it — Unity draws
                    // it as pink rather than crashing, but pink squares in a gallery are indistinguishable
                    // from a rendering bug, and pointing at destroyed memory is not a thing to leave lying
                    // around because it happens to be survivable.
                    _cellImages[i].texture = null;
                    continue;
                }

                CapturedShot shot = shots[shotIndex];
                _cells[i].SetActive(true);

                RawImage picture = _cellImages[i];
                picture.texture = shot.Image;

                // A shot stored without a picture (no camera) still gets a cell — a flat plate, so the entry
                // reads as "no picture" rather than as a black photograph.
                picture.color = shot.HasImage ? Color.white : EmptyImageColor;

                Text label = _cellLabels[i];
                if (label == null) continue;

                label.text = DescribeShot(shot);
                label.color = shot.Grade.IsMiss ? MissTextColor : CountedTextColor;
            }
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
        private static string DescribeShot(CapturedShot shot)
        {
            ShotGrade grade = shot.Grade;
            string who = shot.HasSubject ? shot.SubjectId : "nobody";

            if (grade.IsPlaceholder)
                return $"{Stars(grade.Stars)}\nnot graded";

            if (grade.IsMiss)
                return $"{Stars(grade.Stars)}\nmissed — {Describe(grade.MissReason)}";

            return $"{Stars(grade.Stars)}\n{grade.Percent01:P0}  ·  {who}";
        }

        /// <summary>Five glyphs, always — filled up to the rating. A fixed width so the ratings line up down
        /// the grid and can be compared at a glance, which three characters of "3/5" cannot.</summary>
        private static string Stars(int stars)
        {
            int filled = Mathf.Clamp(stars, 0, 5);
            return new string('★', filled) + new string('☆', 5 - filled);
        }

        /// <summary>Plain words for a miss reason. The enum names are written for developers reading a log;
        /// a player looking at their own photograph should be told what went wrong, in the terms they were
        /// thinking in when they took it.</summary>
        private static string Describe(GradeMiss miss)
        {
            switch (miss)
            {
                case GradeMiss.NoSubject:        return "nobody in frame";
                case GradeMiss.TooSmall:         return "too far away";
                case GradeMiss.Occluded:         return "something in the way";
                case GradeMiss.OutsideFrustum:   return "out of frame";
                case GradeMiss.BehindCamera:     return "behind the lens";
                case GradeMiss.DegenerateBounds: return "nothing to photograph";
                case GradeMiss.NoCamera:         return "no camera";
                case GradeMiss.NoConfig:         return "grading not set up";
                case GradeMiss.NoViewport:       return "no viewport";
                case GradeMiss.Unevaluated:      return "not graded";
                default:                         return miss.ToString();
            }
        }
    }
}
