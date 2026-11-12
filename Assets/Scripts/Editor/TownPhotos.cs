using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2: eye-level photographs of static town geometry, in EDIT mode, from a shot list
    /// (<c>_bmad-output/verification/2-2-walk-the-whole-town/&lt;set&gt;/shots.json</c>). Each shot stands a camera at a
    /// person's eye (ground + the PlayerConfig eye height) and can pose a 1.77 m magenta "person" capsule beside the
    /// subject, so a reader can judge size at a glance. photos.json records every pose and the person's measured
    /// height on screen. Read-only: hidden temporary objects, the scene is never saved (reopened from disk if dirtied).
    /// Animators, physics and actors do not run here — static geometry only.
    /// </summary>
    public static class TownPhotos
    {
        [System.Serializable] public class Shot { public string name; public float[] eye; public float[] look; public float[] person; public float fov = 60f; public float eyeHeight = -1f; public float[] lookAt; }
        [System.Serializable] public class ShotList { public Shot[] shots; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(float v) => v.ToString("0.###", Inv);

        [MenuItem("Tools/Town/Photos - census set (edit mode)")] public static void Census() => Run("census/photos");
        [MenuItem("Tools/Town/Photos - colliders set (edit mode)")] public static void Colliders() => Run("colliders/photos");
        [MenuItem("Tools/Town/Photos - parts set (edit mode)")] public static void Parts() => Run("census/parts-photos");

        public static void Run(string set)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Town] Photos run in edit mode."); return; }
            string dir = Path.Combine(TownCensus.Root, set).Replace('\\', '/');
            string list = Path.Combine(dir, "shots.json");
            if (!File.Exists(list)) { Debug.LogWarning("[Town] No shot list at " + list); return; }
            Scene scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            Shot[] shots = JsonUtility.FromJson<ShotList>(File.ReadAllText(list)).shots;
            GameObject map = GameObject.Find("game map");
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);
            PhotoShootRig.ReadPlayerEye(out float eye, out float near, out string src);
            var meta = new StringBuilder("{\n \"eye_source\": \"" + src + "\", \"shots\": [\n");
            var temp = new List<Object>();
            const int W = 1280, H = 720;
            try
            {
                Physics.SyncTransforms();
                var camGo = new GameObject("TownPhotos cam (temp)") { hideFlags = HideFlags.HideAndDontSave };
                temp.Add(camGo);
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.nearClipPlane = near; cam.farClipPlane = 2000f;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                temp.Add(rt);
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                temp.Add(tex);
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.magenta };
                temp.Add(mat);
                for (int s = 0; s < shots.Length; s++)
                {
                    Shot sh = shots[s];
                    GameObject person = null;
                    if (sh.person != null && sh.person.Length >= 2)
                    {
                        person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        person.hideFlags = HideFlags.HideAndDontSave;
                        Object.DestroyImmediate(person.GetComponent<Collider>());
                        person.GetComponent<Renderer>().sharedMaterial = mat;
                        float gy = Ground(kinds, sh.person[0], sh.person[1]);
                        person.transform.position = new Vector3(sh.person[0], gy + 0.885f, sh.person[1]);
                        person.transform.localScale = new Vector3(0.6f, 0.885f, 0.6f);   // a 1.77 m tall, 0.30 m radius capsule
                    }
                    float ey = Ground(kinds, sh.eye[0], sh.eye[1]) + (sh.eyeHeight > 0f ? sh.eyeHeight : eye);
                    if (sh.eye.Length >= 3 && !float.IsNaN(sh.eye[2]) && sh.eye[2] > -9000f) ey = sh.eye[2];
                    Vector3 p = new Vector3(sh.eye[0], ey, sh.eye[1]);
                    Vector3 target;
                    if (sh.look != null && sh.look.Length >= 3) target = new Vector3(sh.look[0], sh.look[1], sh.look[2]);
                    else if (sh.look != null && sh.look.Length == 2) target = new Vector3(sh.look[0], Ground(kinds, sh.look[0], sh.look[1]) + 1f, sh.look[1]);
                    else target = p + Vector3.forward;
                    cam.fieldOfView = sh.fov;
                    camGo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(target - p));
                    cam.targetTexture = rt;
                    cam.aspect = (float)W / H;
                    cam.Render();
                    cam.targetTexture = null;
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, sh.name + ".jpg"), tex.EncodeToJPG(90));
                    string personPx = "null";
                    if (person != null)
                    {
                        Vector3 top = cam.WorldToScreenPoint(person.transform.position + Vector3.up * 0.885f);
                        Vector3 bot = cam.WorldToScreenPoint(person.transform.position - Vector3.up * 0.885f);
                        personPx = $"{{\"x\": {F(bot.x)}, \"top_y_from_top\": {F(H - top.y)}, \"bottom_y_from_top\": {F(H - bot.y)}, \"metres\": [{F(person.transform.position.x)}, {F(person.transform.position.y - 0.885f)}, {F(person.transform.position.z)}]}}";
                        Object.DestroyImmediate(person);
                    }
                    meta.Append($"  {{\"name\": \"{sh.name}\", \"camera\": [{F(p.x)}, {F(p.y)}, {F(p.z)}], \"target\": [{F(target.x)}, {F(target.y)}, {F(target.z)}], \"fov\": {F(sh.fov)}, \"person\": {personPx}}}{(s < shots.Length - 1 ? "," : "")}\n");
                }
            }
            finally
            {
                foreach (Object o in temp) if (o != null) Object.DestroyImmediate(o);
                meta.Append(" ]\n}\n");
                File.WriteAllText(Path.Combine(dir, "photos.json"), meta.ToString());
                if (!wasDirty && scene.isDirty && File.Exists(scene.path))
                    EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
                Debug.Log($"[Town] Photos done → {dir}");
            }
        }

        /// <summary>The top surface at a column that is neither scatter nor water (the census rule).</summary>
        public static float Ground(Dictionary<Collider, int> kinds, float x, float z)
        {
            TownCensus.Column(kinds, x, z, out float sy, out _, out float wy, out _, out _);
            if (float.IsNaN(sy)) return float.IsNaN(wy) ? 0f : wy;
            return sy;
        }
    }
}
