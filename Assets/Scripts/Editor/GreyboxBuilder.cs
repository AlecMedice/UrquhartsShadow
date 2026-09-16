using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UrquhartsShadow.Audio;
using UrquhartsShadow.Boat;
using UrquhartsShadow.Config;
using UrquhartsShadow.Core;
using UrquhartsShadow.Environment;
using UrquhartsShadow.Nessie;
using UrquhartsShadow.Networking;
using UrquhartsShadow.Player;
using UrquhartsShadow.Tools;
using UrquhartsShadow.UI;

namespace UrquhartsShadow.Editor
{
    /// <summary>
    /// Builds a playable greybox of the whole game from primitives: four scenes, the player / vessel / Nessie /
    /// beacon / ROV / PersistentSystems prefabs, a water mesh + material, and Build Settings.
    /// Menu: Urquhart's Shadow > Setup > Build Greybox (All). Safe to re-run; it overwrites the generated assets.
    /// Everything it creates is a placeholder to be replaced by real art; the wiring is the point.
    /// </summary>
    public static class GreyboxBuilder
    {
        private const string Gen = "Assets/Generated";
        private const string Prefabs = Gen + "/Prefabs";
        private const string Scenes = "Assets/Scenes";

        [MenuItem("Urquhart's Shadow/Setup/Build Greybox (All)")]
        public static void BuildAll()
        {
            ProjectSetupMenu.CreateConfigAssets();
            ProjectSetupMenu.CreateTagsAndLayers();
            Directory.CreateDirectory(Gen); Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Scenes);
            EnsureUrp();

            var water = BuildWaterAssets();
            var beaconPrefab = BuildBeaconPrefab();
            var playerPrefab = BuildPlayerPrefab();
            var nessiePrefab = BuildNessiePrefab();
            var rovPrefab = BuildRovPrefab();
            var vesselPrefab = BuildVesselPrefab(beaconPrefab, rovPrefab);
            var persistent = BuildPersistentSystemsPrefab(new[] { playerPrefab, nessiePrefab, rovPrefab, vesselPrefab, beaconPrefab });

            string boot = BuildBootstrapScene(persistent);
            string title = BuildTitleScene();
            string loch = BuildLochScene(water, playerPrefab, nessiePrefab, vesselPrefab);
            string ending = BuildEndingScene();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(boot, true), new EditorBuildSettingsScene(title, true),
                new EditorBuildSettingsScene(loch, true), new EditorBuildSettingsScene(ending, true)
            };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(boot);
            Debug.Log("[Greybox] Done. Press Play from the Bootstrap scene, choose Solo.");
        }

        // ------------------------------------------------------------------ helpers
        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[Greybox] {target.GetType().Name} has no field '{field}'"); return; }
            p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArray(Object target, string field, IList<Object> values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[Greybox] {target.GetType().Name} has no field '{field}'"); return; }
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetInt(Object target, string field, int v)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(field);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v; else p.intValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetBool(Object target, string field, bool v)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(field);
            if (p == null) return; p.boolValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetString(Object target, string field, string v)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(field);
            if (p == null) return; p.stringValue = v; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 scale, Color? color = null)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localScale = scale;
            if (color.HasValue) go.GetComponent<Renderer>().sharedMaterial = Mat(name + "Mat", color.Value);
            return go;
        }
        private static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; return go;
        }
        private static Material Mat(string name, Color c)
        {
            string path = $"{Gen}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh); AssetDatabase.CreateAsset(m, path);
            }
            m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
        private static void SetLayerRecursive(GameObject go, string layer)
        {
            int l = LayerMask.NameToLayer(layer); if (l < 0) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = l;
        }
        private static GameObject SavePrefab(GameObject go, string name)
        {
            string path = $"{Prefabs}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
        private static NetworkTransform AddNetTransform(GameObject go, bool ownerAuthority)
        {
            var nt = go.AddComponent<NetworkTransform>();
            var so = new SerializedObject(nt); var p = so.FindProperty("AuthorityMode");
            if (p != null) { p.enumValueIndex = ownerAuthority ? 1 : 0; so.ApplyModifiedPropertiesWithoutUndo(); }
            return nt;
        }
        private static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, $"{Gen}/URP_Renderer.asset");
            var asset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(asset, $"{Gen}/URP_Asset.asset");
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            Debug.Log("[Greybox] Created and assigned a URP asset.");
        }

        // ------------------------------------------------------------------ water
        private static (Mesh mesh, Material mat) BuildWaterAssets()
        {
            const int n = 160; const float size = 1200f;
            var mesh = new Mesh { name = "LochWaterMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var verts = new Vector3[(n + 1) * (n + 1)]; var uvs = new Vector2[verts.Length];
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                int i = z * (n + 1) + x;
                verts[i] = new Vector3((x / (float)n - 0.5f) * size, 0f, (z / (float)n - 0.5f) * size);
                uvs[i] = new Vector2(x / (float)n, z / (float)n);
            }
            var tris = new int[n * n * 6]; int t = 0;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 20f, size));
            AssetDatabase.CreateAsset(mesh, $"{Gen}/LochWaterMesh.asset");

            var shader = Shader.Find("UrquhartsShadow/LochWater") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "LochWater" };
            AssetDatabase.CreateAsset(mat, $"{Gen}/LochWater.mat");
            return (mesh, mat);
        }

        // ------------------------------------------------------------------ prefabs
        private static GameObject BuildBeaconPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SonarBeacon"; go.transform.localScale = Vector3.one * 0.8f; go.tag = GameConstants.TagDeployable;
            go.GetComponent<Renderer>().sharedMaterial = Mat("BeaconMat", new Color(0.9f, 0.5f, 0.1f));
            go.AddComponent<NetworkObject>(); AddNetTransform(go, false);
            var light = Empty("Light", go.transform, Vector3.up * 0.6f).AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.2f); light.range = 12f; light.intensity = 2f;
            var beacon = go.AddComponent<SonarBeacon>();
            Set(beacon, "beaconLight", light);
            SetLayerRecursive(go, GameConstants.LayerDeployable);
            return SavePrefab(go, "SonarBeacon");
        }

        private static GameObject BuildPlayerPrefab()
        {
            var go = new GameObject("Player") { tag = GameConstants.TagPlayer };
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.AddComponent<NetworkObject>(); AddNetTransform(go, true);

            var body = Prim(PrimitiveType.Capsule, "Body", go.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), new Color(0.35f, 0.3f, 0.25f));
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var camRoot = Empty("CameraRoot", go.transform, new Vector3(0f, 1.6f, 0f));
            var cam = camRoot.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.fieldOfView = 75f;
            var listener = camRoot.AddComponent<AudioListener>();
            var hand = Empty("HandSocket", camRoot.transform, new Vector3(0.3f, -0.25f, 0.5f));
            var phoneGo = Prim(PrimitiveType.Cube, "Phone", hand.transform, Vector3.zero, new Vector3(0.07f, 0.14f, 0.01f), Color.black);
            Object.DestroyImmediate(phoneGo.GetComponent<Collider>());
            var phone = phoneGo.AddComponent<PhoneCamera>();
            Set(phone, "viewModel", phoneGo);
            var flash = Empty("Flashlight", camRoot.transform, new Vector3(0f, -0.1f, 0.2f)).AddComponent<Light>();
            flash.type = LightType.Spot; flash.range = 40f; flash.spotAngle = 45f; flash.intensity = 6f; flash.enabled = false;

            var pc = go.AddComponent<PlayerCharacter>();
            var input = go.AddComponent<InputReader>();
            go.AddComponent<PlayerMovement>(); var look = go.AddComponent<PlayerLook>();
            go.AddComponent<PlayerVitals>(); go.AddComponent<PlayerInteractor>();
            go.AddComponent<PlayerEvidenceBag>(); go.AddComponent<PlayerInventory>(); go.AddComponent<SpectatorCamera>();

            Set(pc, "playerCamera", cam); Set(pc, "listener", listener); Set(pc, "handSocket", hand.transform); Set(pc, "flashlight", flash);
            SetArray(pc, "hideForOwner", new Object[] { body.GetComponent<Renderer>() });
            Set(look, "cameraRoot", camRoot.transform);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/PlayerControls.inputactions");
            if (actions != null) Set(input, "actions", actions); else Debug.LogWarning("[Greybox] PlayerControls.inputactions not found.");
            SetLayerRecursive(go, GameConstants.LayerPlayer);
            return SavePrefab(go, "Player");
        }

        private static GameObject BuildNessiePrefab()
        {
            var go = new GameObject("Nessie") { tag = GameConstants.TagNessie };
            go.AddComponent<NetworkObject>(); AddNetTransform(go, false);
            var bodyMat = new Color(0.08f, 0.1f, 0.09f);
            var torso = Prim(PrimitiveType.Capsule, "Torso", go.transform, Vector3.zero, new Vector3(3f, 6f, 3f), bodyMat);
            torso.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var neck = Prim(PrimitiveType.Capsule, "Neck", go.transform, new Vector3(0f, 1.5f, 6f), new Vector3(1f, 2.5f, 1f), bodyMat);
            neck.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            var head = Prim(PrimitiveType.Cube, "Head", go.transform, new Vector3(0f, 3.5f, 8f), new Vector3(1.2f, 0.9f, 2f), bodyMat);
            var eyeL = Prim(PrimitiveType.Sphere, "EyeL", head.transform, new Vector3(0.4f, 0.3f, 0.3f), new Vector3(0.15f, 0.2f, 0.1f), new Color(0.9f, 0.85f, 0.4f));
            var eyeR = Prim(PrimitiveType.Sphere, "EyeR", head.transform, new Vector3(-0.4f, 0.3f, 0.3f), new Vector3(0.15f, 0.2f, 0.1f), new Color(0.9f, 0.85f, 0.4f));
            Object.DestroyImmediate(eyeL.GetComponent<Collider>()); Object.DestroyImmediate(eyeR.GetComponent<Collider>());
            var voice = go.AddComponent<AudioSource>(); voice.spatialBlend = 1f; voice.maxDistance = 400f;
            var splash = go.AddComponent<AudioSource>(); splash.spatialBlend = 1f; splash.maxDistance = 300f;
            var body = go.AddComponent<NessieBody>();
            Set(body, "head", head.transform); Set(body, "voice", voice); Set(body, "splash", splash);
            var ai = go.AddComponent<NessieAI>();
            SetBool(ai, "verbose", true);
            SetLayerRecursive(go, GameConstants.LayerNessie);
            return SavePrefab(go, "Nessie");
        }

        private static GameObject BuildRovPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ROV"; go.transform.localScale = new Vector3(0.8f, 0.5f, 1.2f);
            go.GetComponent<Renderer>().sharedMaterial = Mat("RovMat", new Color(0.9f, 0.8f, 0.1f));
            go.AddComponent<NetworkObject>(); AddNetTransform(go, false);
            var camGo = Empty("RovCamera", go.transform, new Vector3(0f, 0f, 0.6f));
            var cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 80f; cam.nearClipPlane = 0.05f;
            var flood = Empty("Flood", go.transform, new Vector3(0f, 0.2f, 0.5f)).AddComponent<Light>();
            flood.type = LightType.Spot; flood.range = 30f; flood.spotAngle = 70f; flood.intensity = 8f;
            var tether = go.AddComponent<LineRenderer>(); tether.widthMultiplier = 0.05f; tether.positionCount = 2;
            tether.sharedMaterial = Mat("TetherMat", Color.gray);
            var thr = go.AddComponent<AudioSource>(); thr.spatialBlend = 1f; thr.loop = true;
            var rov = go.AddComponent<ROV>();
            Set(rov, "rovCamera", cam); Set(rov, "floodLight", flood); Set(rov, "tether", tether); Set(rov, "thrusters", thr);
            SetLayerRecursive(go, GameConstants.LayerDeployable);
            return SavePrefab(go, "ROV");
        }

        private static GameObject BuildVesselPrefab(GameObject beaconPrefab, GameObject rovPrefab)
        {
            var go = new GameObject("Vessel") { tag = GameConstants.TagVessel };
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 20000f; rb.linearDamping = 0.5f; rb.angularDamping = 1f;
            rb.centerOfMass = new Vector3(0f, -1.5f, 0f);
            go.AddComponent<NetworkObject>(); AddNetTransform(go, false);
            var hullColor = new Color(0.25f, 0.28f, 0.3f);
            var deckColor = new Color(0.4f, 0.32f, 0.22f);

            // Hull: visual box without a collider, plus thin wall colliders so the lower deck is walkable.
            var hullVis = Prim(PrimitiveType.Cube, "Hull", go.transform, new Vector3(0f, -1.2f, 0f), new Vector3(9f, 3f, 30f), hullColor);
            Object.DestroyImmediate(hullVis.GetComponent<Collider>());
            foreach (var (n, pos, size) in new[]
            {
                ("HullFloor", new Vector3(0f, -2.6f, 0f), new Vector3(9f, 0.2f, 30f)),
                ("HullSideL", new Vector3(4.4f, -1.2f, 0f), new Vector3(0.2f, 3f, 30f)),
                ("HullSideR", new Vector3(-4.4f, -1.2f, 0f), new Vector3(0.2f, 3f, 30f)),
                ("HullBow", new Vector3(0f, -1.2f, 14.9f), new Vector3(9f, 3f, 0.2f)),
                ("HullStern", new Vector3(0f, -1.2f, -14.9f), new Vector3(9f, 3f, 0.2f)),
            })
            {
                var wall = Empty(n, go.transform, pos); var bc = wall.AddComponent<BoxCollider>(); bc.size = size;
            }
            // Main deck in three slabs, leaving a hatch hole at x 1.5..4.5, z -4..1 for the stairs.
            Prim(PrimitiveType.Cube, "DeckFwd", go.transform, new Vector3(0f, 0.5f, 8f), new Vector3(9f, 0.4f, 14f), deckColor);
            Prim(PrimitiveType.Cube, "DeckAft", go.transform, new Vector3(0f, 0.5f, -9.5f), new Vector3(9f, 0.4f, 11f), deckColor);
            Prim(PrimitiveType.Cube, "DeckMid", go.transform, new Vector3(-1.5f, 0.5f, -1.5f), new Vector3(6f, 0.4f, 5f), deckColor);
            Prim(PrimitiveType.Cube, "WheelhouseFloor", go.transform, new Vector3(0f, 0.75f, 6f), new Vector3(6.8f, 0.1f, 7.8f), deckColor);
            var whColor = new Color(0.6f, 0.6f, 0.62f);
            Prim(PrimitiveType.Cube, "WH_Roof", go.transform, new Vector3(0f, 3.7f, 6f), new Vector3(7f, 0.2f, 8f), whColor);
            Prim(PrimitiveType.Cube, "WH_WallL", go.transform, new Vector3(3.4f, 2.2f, 6f), new Vector3(0.2f, 3f, 8f), whColor);
            Prim(PrimitiveType.Cube, "WH_WallR", go.transform, new Vector3(-3.4f, 2.2f, 6f), new Vector3(0.2f, 3f, 8f), whColor);
            Prim(PrimitiveType.Cube, "WH_WallFwd", go.transform, new Vector3(0f, 2.2f, 10f), new Vector3(7f, 3f, 0.2f), whColor);
            Prim(PrimitiveType.Cube, "WH_WallAftL", go.transform, new Vector3(2.4f, 2.2f, 2f), new Vector3(2.2f, 3f, 0.2f), whColor);
            Prim(PrimitiveType.Cube, "WH_WallAftR", go.transform, new Vector3(-2.4f, 2.2f, 2f), new Vector3(2.2f, 3f, 0.2f), whColor);
            // Rails
            Prim(PrimitiveType.Cube, "RailL", go.transform, new Vector3(4.4f, 1.2f, 0f), new Vector3(0.1f, 1f, 30f), Color.gray);
            Prim(PrimitiveType.Cube, "RailR", go.transform, new Vector3(-4.4f, 1.2f, 0f), new Vector3(0.1f, 1f, 30f), Color.gray);
            Prim(PrimitiveType.Cube, "RailAft", go.transform, new Vector3(0f, 1.2f, -14.9f), new Vector3(9f, 1f, 0.1f), Color.gray);
            // Lower deck: floor plus a ramp from the hatch hole down to it.
            Prim(PrimitiveType.Cube, "LowerDeckFloor", go.transform, new Vector3(0f, -2.4f, -6f), new Vector3(8.6f, 0.2f, 12f), deckColor);
            var stairs = Prim(PrimitiveType.Cube, "HatchStairs", go.transform, new Vector3(3f, -0.85f, -2f), new Vector3(2.6f, 0.2f, 5.6f), deckColor);
            stairs.transform.localRotation = Quaternion.Euler(-32f, 0f, 0f);

            // Buoyancy floaters
            var floaters = new List<Object>();
            foreach (var p in new[] { new Vector3(3.5f, -2f, 12f), new Vector3(-3.5f, -2f, 12f), new Vector3(3.5f, -2f, -12f), new Vector3(-3.5f, -2f, -12f) })
                floaters.Add(Empty("Floater", go.transform, p).transform);
            var buoy = go.AddComponent<Buoyancy>(); SetArray(buoy, "floaters", floaters);

            // Rail points
            var rails = new List<Object>();
            for (int i = -2; i <= 2; i++) { rails.Add(Empty("RailPt", go.transform, new Vector3(4.2f, 0.8f, i * 6f)).transform); rails.Add(Empty("RailPt", go.transform, new Vector3(-4.2f, 0.8f, i * 6f)).transform); }
            rails.Add(Empty("RailPt", go.transform, new Vector3(0f, 0.8f, -14.5f)).transform);

            // Stations
            var helm = MakeStation<Helm>(go.transform, "Helm", new Vector3(0f, 1.2f, 9f), new Color(0.5f, 0.3f, 0.1f));
            var sonar = MakeStation<SideScanSonar>(go.transform, "Sonar console", new Vector3(2.6f, 1.2f, 8f), new Color(0.1f, 0.4f, 0.2f));
            var hydro = MakeStation<HydrophoneArray>(go.transform, "Hydrophone console", new Vector3(-2.6f, 1.2f, 8f), new Color(0.1f, 0.2f, 0.5f));
            var tele = MakeStation<TelephotoCamera>(go.transform, "35mm telephoto", new Vector3(0f, 1.2f, -12f), Color.black);
            var view = Empty("ViewPoint", tele.transform, new Vector3(0f, 0.9f, 0f));
            var scope = view.AddComponent<Camera>(); scope.enabled = false; scope.fieldOfView = 12f; scope.nearClipPlane = 0.1f;
            Set(tele, "viewPoint", view.transform); Set(tele, "scopeCamera", scope);
            var rovStation = MakeStation<ROVStation>(go.transform, "ROV console", new Vector3(-2.6f, 1.2f, 4f), new Color(0.5f, 0.45f, 0.1f));

            // Other deck gear
            var locker = Prim(PrimitiveType.Cube, "EvidenceLocker", go.transform, new Vector3(2.6f, 1.2f, 4f), new Vector3(1f, 1.2f, 0.8f), new Color(0.6f, 0.1f, 0.1f));
            locker.AddComponent<NetworkObject>(); locker.AddComponent<EvidenceLocker>();
            var launchers = new List<Object>();
            foreach (var (pos, yaw) in new[] { (new Vector3(4f, 1.2f, -4f), 90f), (new Vector3(-4f, 1.2f, -4f), -90f) })
            {
                var l = Prim(PrimitiveType.Cylinder, "BeaconLauncher", go.transform, pos, new Vector3(0.4f, 0.6f, 0.4f), new Color(0.3f, 0.3f, 0.35f));
                l.transform.localRotation = Quaternion.Euler(0f, yaw, 90f);
                var muzzle = Empty("Muzzle", go.transform, pos + new Vector3(Mathf.Sign(pos.x) * 1f, 0.5f, 0f)); muzzle.transform.localRotation = Quaternion.Euler(-20f, yaw, 0f);
                l.AddComponent<NetworkObject>(); var bl = l.AddComponent<BeaconLauncher>();
                Set(bl, "beaconPrefab", beaconPrefab); Set(bl, "muzzle", muzzle.transform);
                launchers.Add(bl);
            }
            var samplerGo = Prim(PrimitiveType.Cube, "EdnaSampler", go.transform, new Vector3(4f, 1.2f, -9f), new Vector3(0.6f, 1f, 0.6f), new Color(0.2f, 0.5f, 0.5f));
            samplerGo.AddComponent<NetworkObject>(); var sampler = samplerGo.AddComponent<EdnaSampler>();
            Set(sampler, "vialDropPoint", Empty("VialDrop", go.transform, new Vector3(5.5f, -1.5f, -9f)).transform);
            var ladderGo = Prim(PrimitiveType.Cube, "Ladder", go.transform, new Vector3(4.6f, -0.5f, -12f), new Vector3(0.2f, 3f, 0.8f), Color.gray);
            var ladder = ladderGo.AddComponent<Ladder>(); Set(ladder, "topPoint", Empty("LadderTop", go.transform, new Vector3(3.6f, 0.8f, -12f)).transform);
            var rationGo = Prim(PrimitiveType.Cube, "Galley", go.transform, new Vector3(2.5f, -1.7f, -8f), new Vector3(1.2f, 1.2f, 1.2f), new Color(0.6f, 0.5f, 0.3f));
            rationGo.AddComponent<NetworkObject>(); var rations = rationGo.AddComponent<SupplyStation>(); SetInt(rations, "kind", 0);
            var battGo = Prim(PrimitiveType.Cube, "BatteryLocker", go.transform, new Vector3(-2.5f, -1.7f, -8f), new Vector3(1.2f, 1.2f, 1.2f), new Color(0.2f, 0.6f, 0.3f));
            battGo.AddComponent<NetworkObject>(); var batts = battGo.AddComponent<SupplyStation>(); SetInt(batts, "kind", 1);
            foreach (var p in new[] { new Vector3(3.2f, -1.8f, -3f), new Vector3(-3.2f, -1.8f, -10f) })
            {
                var r = Prim(PrimitiveType.Cube, "RepairPoint", go.transform, p, new Vector3(0.5f, 0.8f, 0.5f), new Color(0.7f, 0.4f, 0.1f));
                r.AddComponent<NetworkObject>(); r.AddComponent<RepairPoint>();
            }

            // Monitors
            MakeMonitor(go.transform, "Monitor_Beacons", new Vector3(2.5f, 2.6f, 9.8f), 0, null);
            MakeMonitor(go.transform, "Monitor_Vessel", new Vector3(0f, 2.6f, 9.8f), 2, null);
            var rovMonitor = MakeMonitor(go.transform, "Monitor_ROV", new Vector3(-2.5f, 2.6f, 9.8f), 1, null);

            // Spawn points on deck
            var spawns = new List<Object>();
            for (int i = 0; i < 5; i++) spawns.Add(Empty($"Spawn{i}", go.transform, new Vector3(-2f + i, 0.8f, -2f)).transform);
            var spawnRoot = Empty("SpawnPoints", go.transform, Vector3.zero);
            foreach (Transform s in spawns) s.SetParent(spawnRoot.transform, true);

            // Docked ROV instance
            var rovInst = (GameObject)PrefabUtility.InstantiatePrefab(rovPrefab);
            rovInst.transform.SetParent(go.transform, false); rovInst.transform.localPosition = new Vector3(-3.5f, 1f, -1f);
            var rov = rovInst.GetComponent<ROV>();
            Set(rov, "tetherAnchorOnVessel", Empty("TetherAnchor", go.transform, new Vector3(-4.5f, 1f, -1f)).transform);
            Set(rovStation, "rov", rov); Set(rovMonitor, "rov", rov);

            var engine = go.AddComponent<AudioSource>(); engine.loop = true; engine.spatialBlend = 1f; engine.maxDistance = 200f;
            var impact = go.AddComponent<AudioSource>(); impact.spatialBlend = 1f; impact.maxDistance = 300f;

            var vessel = go.AddComponent<ResearchVessel>();
            SetArray(vessel, "railPoints", rails); Set(vessel, "buoyancy", buoy); Set(vessel, "telephoto", tele);
            SetArray(vessel, "launchers", launchers); SetArray(vessel, "samplers", new Object[] { sampler });
            Set(vessel, "rov", rov); Set(vessel, "supplyStation", rations); Set(vessel, "sonar", sonar); Set(vessel, "hydrophone", hydro);
            Set(vessel, "engineAudio", engine); Set(vessel, "hullImpactAudio", impact);

            SetLayerRecursive(go, GameConstants.LayerVessel);
            foreach (var st in go.GetComponentsInChildren<IInteractable>()) SetLayerRecursive(((Component)st).gameObject, GameConstants.LayerInteractable);
            return SavePrefab(go, "Vessel");
        }

        private static T MakeStation<T>(Transform parent, string name, Vector3 pos, Color color) where T : Station
        {
            var go = Prim(PrimitiveType.Cube, name, parent, pos, new Vector3(1.2f, 1.2f, 0.6f), color);
            go.AddComponent<NetworkObject>();
            var st = go.AddComponent<T>();
            var seat = Empty("Seat", parent, pos + new Vector3(0f, -0.4f, -0.9f)); seat.transform.LookAt(seat.transform.position + parent.forward);
            Set(st, "seatPoint", seat.transform); SetString(st, "stationName", name);
            return st;
        }

        private static MonitorScreen MakeMonitor(Transform parent, string name, Vector3 pos, int mode, ROV rov)
        {
            var quad = Prim(PrimitiveType.Quad, name, parent, pos, new Vector3(2.2f, 1.2f, 1f), new Color(0.02f, 0.05f, 0.03f));
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            var textGo = new GameObject("Text"); textGo.transform.SetParent(quad.transform, false); textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var tmp = textGo.AddComponent<TextMeshPro>(); tmp.fontSize = 3f; tmp.color = new Color(0.4f, 1f, 0.5f); tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.rectTransform.sizeDelta = new Vector2(0.95f, 0.9f); tmp.textWrappingMode = TextWrappingModes.NoWrap;
            var ms = quad.AddComponent<MonitorScreen>(); SetInt(ms, "mode", mode); Set(ms, "text", tmp); if (rov) Set(ms, "rov", rov);
            return ms;
        }

        private static GameObject BuildPersistentSystemsPrefab(GameObject[] networkPrefabs)
        {
            var go = new GameObject("PersistentSystems");
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp, EnableSceneManagement = true, ConnectionApproval = false };
            RegisterNetworkPrefabs(nm, networkPrefabs);
            go.AddComponent<SessionManager>();
            var am = go.AddComponent<AudioManager>();
            var ambient = go.AddComponent<AudioSource>(); ambient.loop = true; ambient.playOnAwake = false;
            var tension = go.AddComponent<AudioSource>(); tension.loop = true; tension.playOnAwake = false;
            var stinger = go.AddComponent<AudioSource>(); stinger.playOnAwake = false;
            Set(am, "ambientLayer", ambient); Set(am, "tensionLayer", tension); Set(am, "stingerSource", stinger);
            return SavePrefab(go, "PersistentSystems");
        }

        /// <summary>NGO's prefab list API has shifted between versions; use reflection and fall back to a clear message.</summary>
        private static void RegisterNetworkPrefabs(NetworkManager nm, GameObject[] prefabs)
        {
            try
            {
                var listAsset = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(listAsset, $"{Gen}/NetworkPrefabsList.asset");
                var add = typeof(NetworkPrefabsList).GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in prefabs)
                {
                    var np = new NetworkPrefab { Prefab = p };
                    if (add != null) add.Invoke(listAsset, new object[] { np });
                }
                nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(listAsset);
                EditorUtility.SetDirty(listAsset);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Greybox] Could not register network prefabs automatically ({e.Message}). Add Player, Nessie, Vessel, ROV and SonarBeacon to NetworkManager's Network Prefabs list by hand.");
            }
        }

        // ------------------------------------------------------------------ scenes
        private static string BuildBootstrapScene(GameObject persistent)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap"); var b = go.AddComponent<GameBootstrap>();
            Set(b, "persistentSystemsPrefab", persistent);
            var cam = new GameObject("Camera").AddComponent<Camera>(); cam.backgroundColor = Color.black; cam.clearFlags = CameraClearFlags.SolidColor;
            string path = $"{Scenes}/{GameConstants.SceneBootstrap}.unity";
            EditorSceneManager.SaveScene(scene, path); return path;
        }

        private static (Canvas canvas, GameObject root) MakeCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            }
            return (c, go);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, Vector2 anchor, Vector2 pos, float size = 28f, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>(); t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
            var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(700f, 60f);
            return t;
        }

        private static Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.14f, 0.9f);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = MakeText(go.transform, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero, 26f, TextAlignmentOptions.Center);
            t.rectTransform.sizeDelta = size;
            return go.GetComponent<Button>();
        }

        private static TMP_InputField MakeInput(Transform parent, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(placeholder + "Input", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)); area.transform.SetParent(go.transform, false);
            var art = area.GetComponent<RectTransform>(); art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = new Vector2(10, 6); art.offsetMax = new Vector2(-10, -6);
            var text = MakeText(area.transform, "Text", "", Vector2.zero, Vector2.zero, 24f); text.color = Color.black;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            var ph = MakeText(area.transform, "Placeholder", placeholder, Vector2.zero, Vector2.zero, 24f); ph.color = new Color(0.3f, 0.3f, 0.3f); ph.fontStyle = FontStyles.Italic;
            ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one; ph.rectTransform.offsetMin = ph.rectTransform.offsetMax = Vector2.zero;
            var input = go.AddComponent<TMP_InputField>(); input.textViewport = art; input.textComponent = text; input.placeholder = ph;
            return input;
        }

        private static string BuildTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
            var (canvas, root) = MakeCanvas("TitleCanvas");
            var title = root.AddComponent<TitleScreenUI>();

            var main = new GameObject("MainPanel", typeof(RectTransform)); main.transform.SetParent(root.transform, false); Stretch(main);
            MakeText(main.transform, "Title", "URQUHART'S SHADOW", new Vector2(0.5f, 1f), new Vector2(0, -120), 72f, TextAlignmentOptions.Center).rectTransform.sizeDelta = new Vector2(1400, 120);
            MakeText(main.transform, "Sub", "Loch Ness, October 1972", new Vector2(0.5f, 1f), new Vector2(0, -210), 28f, TextAlignmentOptions.Center);
            var solo = MakeButton(main.transform, "Solo Expedition", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(360, 56));
            var multi = MakeButton(main.transform, "Multiplayer", new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(360, 56));
            var settings = MakeButton(main.transform, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(360, 56));
            var credits = MakeButton(main.transform, "Credits", new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(360, 56));
            var quit = MakeButton(main.transform, "Quit", new Vector2(0.5f, 0.5f), new Vector2(0, -220), new Vector2(360, 56));

            var mp = new GameObject("MultiplayerPanel", typeof(RectTransform)); mp.transform.SetParent(root.transform, false); Stretch(mp);
            var mpUi = mp.AddComponent<MultiplayerMenuUI>();
            MakeText(mp.transform, "Header", "MULTIPLAYER", new Vector2(0.5f, 1f), new Vector2(0, -120), 48f, TextAlignmentOptions.Center);
            var nameIn = MakeInput(mp.transform, "Your name", new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(360, 48));
            var host = MakeButton(mp.transform, "Start a game", new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(360, 56));
            var codeIn = MakeInput(mp.transform, "Join code", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(360, 48));
            var join = MakeButton(mp.transform, "Join a game", new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(360, 56));
            var back = MakeButton(mp.transform, "Back", new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(360, 56));
            var status = MakeText(mp.transform, "Status", "", new Vector2(0.5f, 0.5f), new Vector2(0, -230), 22f, TextAlignmentOptions.Center);
            var codeDisplay = MakeText(mp.transform, "CodeDisplay", "", new Vector2(0.5f, 0.5f), new Vector2(0, 210), 30f, TextAlignmentOptions.Center);
            var ddGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources()); ddGo.name = "DifficultyDropdown"; ddGo.transform.SetParent(mp.transform, false);
            var ddRt = ddGo.GetComponent<RectTransform>(); ddRt.anchorMin = ddRt.anchorMax = new Vector2(0.5f, 0.5f); ddRt.anchoredPosition = new Vector2(0, 270); ddRt.sizeDelta = new Vector2(360, 40);
            var dd = ddGo.GetComponent<TMP_Dropdown>();
            Set(mpUi, "hostButton", host); Set(mpUi, "joinButton", join); Set(mpUi, "backButton", back); Set(mpUi, "joinCodeInput", codeIn);
            Set(mpUi, "playerNameInput", nameIn); Set(mpUi, "difficultyDropdown", dd); Set(mpUi, "statusText", status); Set(mpUi, "joinCodeDisplay", codeDisplay); Set(mpUi, "title", title);

            var sp = new GameObject("SettingsPanel", typeof(RectTransform)); sp.transform.SetParent(root.transform, false); Stretch(sp);
            var sUi = sp.AddComponent<SettingsUI>();
            MakeText(sp.transform, "Header", "SETTINGS (wire sliders here)", new Vector2(0.5f, 1f), new Vector2(0, -120), 40f, TextAlignmentOptions.Center);
            var sBack = MakeButton(sp.transform, "Back", new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(360, 56));
            Set(sUi, "backButton", sBack); Set(sUi, "title", title);

            var cp = new GameObject("CreditsPanel", typeof(RectTransform)); cp.transform.SetParent(root.transform, false); Stretch(cp);
            MakeText(cp.transform, "Credits", "Urquhart's Shadow\n\nA game about a lake and what lives in it.", new Vector2(0.5f, 0.5f), Vector2.zero, 30f, TextAlignmentOptions.Center).rectTransform.sizeDelta = new Vector2(1200, 400);
            var cBack = MakeButton(cp.transform, "Back", new Vector2(0.5f, 0.5f), new Vector2(0, -260), new Vector2(360, 56));
            UnityEditor.Events.UnityEventTools.AddPersistentListener(cBack.onClick, title.ShowMain);

            Set(title, "mainPanel", main); Set(title, "multiplayerPanel", mp); Set(title, "settingsPanel", sp); Set(title, "creditsPanel", cp);
            Set(title, "soloButton", solo); Set(title, "multiplayerButton", multi); Set(title, "settingsButton", settings); Set(title, "creditsButton", credits); Set(title, "quitButton", quit);
            mp.SetActive(false); sp.SetActive(false); cp.SetActive(false);

            string path = $"{Scenes}/{GameConstants.SceneTitle}.unity";
            EditorSceneManager.SaveScene(scene, path); return path;
        }

        private static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static string BuildLochScene((Mesh mesh, Material mat) water, GameObject playerPrefab, GameObject nessiePrefab, GameObject vesselPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.03f, 0.04f, 0.06f); RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.09f);

            var fallbackCam = new GameObject("FallbackCamera").AddComponent<Camera>(); fallbackCam.depth = -10f; fallbackCam.transform.position = new Vector3(0f, 12f, -40f); fallbackCam.transform.LookAt(Vector3.zero);
            int lNessie = LayerMask.NameToLayer(GameConstants.LayerNessie), lVessel = LayerMask.NameToLayer(GameConstants.LayerVessel), lPlayer = LayerMask.NameToLayer(GameConstants.LayerPlayer), lInter = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            if (lNessie >= 0 && lVessel >= 0) Physics.IgnoreLayerCollision(lNessie, lVessel, true);
            if (lNessie >= 0 && lPlayer >= 0) Physics.IgnoreLayerCollision(lNessie, lPlayer, true);
            if (lNessie >= 0 && lInter >= 0) Physics.IgnoreLayerCollision(lNessie, lInter, true);
            var moon = new GameObject("Moon").AddComponent<Light>(); moon.type = LightType.Directional; moon.color = new Color(0.7f, 0.8f, 1f); moon.intensity = 0.35f;
            moon.transform.rotation = Quaternion.Euler(40f, 210f, 0f); moon.shadows = LightShadows.Soft; RenderSettings.sun = moon;

            // Water
            var waterGo = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer)); waterGo.tag = GameConstants.TagWater;
            waterGo.GetComponent<MeshFilter>().sharedMesh = water.mesh; waterGo.GetComponent<MeshRenderer>().sharedMaterial = water.mat;
            var ocean = waterGo.AddComponent<OceanSurface>(); Set(ocean, "waterMaterial", water.mat);
            SetLayerRecursive(waterGo, GameConstants.LayerWater);
            // Loch floor and shore ring (visual + collision for the ROV)
            var floor = Prim(PrimitiveType.Cube, "LochFloor", null, new Vector3(0f, -220f, 0f), new Vector3(3000f, 1f, 3000f), new Color(0.05f, 0.05f, 0.04f));
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f; var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1500f;
                Prim(PrimitiveType.Cube, "Shore", null, p + Vector3.up * 30f, new Vector3(420f, 120f, 420f), new Color(0.08f, 0.1f, 0.07f)).transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }

            // Bounds + trenches + dock
            var lb = new GameObject("LochBounds").AddComponent<LochBounds>();
            var trenches = new List<Object>();
            foreach (var p in new[] { new Vector3(300f, -200f, 400f), new Vector3(-500f, -200f, -200f), new Vector3(100f, -200f, -700f) })
                trenches.Add(Empty("Trench", lb.transform, p).transform);
            SetArray(lb, "trenchNodes", trenches);
            Set(lb, "dockPosition", Empty("Dock", lb.transform, new Vector3(-1350f, 0f, 0f)).transform);

            // Managers (in-scene NetworkObjects)
            var gm = new GameObject("GameManager"); gm.AddComponent<NetworkObject>(); gm.AddComponent<GameManager>();
            var nc = new GameObject("NightCycleManager"); nc.AddComponent<NetworkObject>(); var ncm = nc.AddComponent<NightCycleManager>(); Set(ncm, "moonLight", moon);
            var wm = new GameObject("WeatherManager"); wm.AddComponent<NetworkObject>(); var wmm = wm.AddComponent<WeatherManager>(); Set(wmm, "ocean", ocean);
            var lightning = Empty("Lightning", wm.transform, new Vector3(0f, 300f, 0f)).AddComponent<Light>(); lightning.type = LightType.Directional; lightning.enabled = false; lightning.color = new Color(0.8f, 0.85f, 1f);
            lightning.transform.rotation = Quaternion.Euler(60f, 30f, 0f); Set(wmm, "lightningLight", lightning);
            var em = new GameObject("EvidenceManager"); em.AddComponent<NetworkObject>(); em.AddComponent<EvidenceManager>();

            // Vessel + spawner
            var vessel = (GameObject)PrefabUtility.InstantiatePrefab(vesselPrefab); vessel.transform.position = new Vector3(0f, 1.5f, 0f);
            var spawner = new GameObject("PlayerSpawner"); spawner.AddComponent<NetworkObject>(); var ps = spawner.AddComponent<PlayerSpawner>();
            var spawnRoot = vessel.transform.Find("SpawnPoints");
            var spawns = new List<Object>(); if (spawnRoot != null) foreach (Transform s in spawnRoot) spawns.Add(s);
            Set(ps, "playerPrefab", playerPrefab); SetArray(ps, "spawnPoints", spawns);

            // Nessie
            var nessie = (GameObject)PrefabUtility.InstantiatePrefab(nessiePrefab); nessie.transform.position = new Vector3(300f, -60f, 400f);

            // HUD
            var (canvas, hudRoot) = MakeCanvas("HUD");
            var hud = hudRoot.AddComponent<HudUI>();
            Set(hud, "nightText", MakeText(hudRoot.transform, "Night", "", new Vector2(0f, 1f), new Vector2(20, -20), 26f));
            Set(hud, "clockText", MakeText(hudRoot.transform, "Clock", "", new Vector2(0f, 1f), new Vector2(20, -55), 22f));
            Set(hud, "evidenceText", MakeText(hudRoot.transform, "Evidence", "", new Vector2(1f, 1f), new Vector2(-20, -20), 26f, TextAlignmentOptions.Right));
            Set(hud, "pendingText", MakeText(hudRoot.transform, "Pending", "", new Vector2(1f, 1f), new Vector2(-20, -55), 20f, TextAlignmentOptions.Right));
            Set(hud, "hullText", MakeText(hudRoot.transform, "Hull", "", new Vector2(1f, 1f), new Vector2(-20, -85), 20f, TextAlignmentOptions.Right));
            Set(hud, "weatherText", MakeText(hudRoot.transform, "Weather", "", new Vector2(1f, 1f), new Vector2(-20, -115), 20f, TextAlignmentOptions.Right));
            Set(hud, "batteryText", MakeText(hudRoot.transform, "Battery", "", new Vector2(0f, 0f), new Vector2(20, 20), 20f));
            Set(hud, "promptText", MakeText(hudRoot.transform, "Prompt", "", new Vector2(0.5f, 0.5f), new Vector2(0, -60), 24f, TextAlignmentOptions.Center));
            Set(hud, "frameQualityText", MakeText(hudRoot.transform, "FrameQuality", "", new Vector2(0.5f, 0.5f), new Vector2(0, 80), 22f, TextAlignmentOptions.Center));
            var rec = MakeText(hudRoot.transform, "REC", "● REC", new Vector2(1f, 0f), new Vector2(-20, 20), 26f, TextAlignmentOptions.Right); rec.color = Color.red; rec.gameObject.SetActive(false);
            Set(hud, "recIndicator", rec.gameObject);
            var toastGo = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup)); toastGo.transform.SetParent(hudRoot.transform, false);
            var toastRt = toastGo.GetComponent<RectTransform>(); toastRt.anchorMin = toastRt.anchorMax = new Vector2(0.5f, 0f); toastRt.anchoredPosition = new Vector2(0, 120); toastRt.sizeDelta = new Vector2(1000, 60);
            var toastText = MakeText(toastGo.transform, "Text", "", new Vector2(0.5f, 0.5f), Vector2.zero, 24f, TextAlignmentOptions.Center); toastText.rectTransform.sizeDelta = new Vector2(1000, 60);
            Set(hud, "toastText", toastText); Set(hud, "toastGroup", toastGo.GetComponent<CanvasGroup>());
            var crosshair = MakeText(hudRoot.transform, "Crosshair", "·", new Vector2(0.5f, 0.5f), Vector2.zero, 40f, TextAlignmentOptions.Center);
            var spec = MakeText(hudRoot.transform, "Spectator", "", new Vector2(0.5f, 1f), new Vector2(0, -20), 24f, TextAlignmentOptions.Center);
            Set(hud, "spectatorPanel", spec.gameObject); Set(hud, "spectatorText", spec);

            // Pause menu
            var pause = hudRoot.AddComponent<PauseMenuUI>();
            var pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image)); pausePanel.transform.SetParent(hudRoot.transform, false); Stretch(pausePanel);
            pausePanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var resume = MakeButton(pausePanel.transform, "Resume", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(360, 56));
            var leave = MakeButton(pausePanel.transform, "Leave expedition", new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(360, 56));
            Set(pause, "panel", pausePanel); Set(pause, "resumeButton", resume); Set(pause, "leaveButton", leave);
            pausePanel.SetActive(false);

            // Dawn shop
            var shop = hudRoot.AddComponent<DawnShopUI>();
            var shopPanel = new GameObject("DawnShopPanel", typeof(RectTransform), typeof(Image)); shopPanel.transform.SetParent(hudRoot.transform, false); Stretch(shopPanel);
            shopPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.04f, 0.85f);
            Set(shop, "fundingText", MakeText(shopPanel.transform, "Funding", "", new Vector2(0.5f, 1f), new Vector2(0, -120), 36f, TextAlignmentOptions.Center));
            var summary = MakeText(shopPanel.transform, "Summary", "", new Vector2(0.5f, 0.5f), new Vector2(0, 120), 24f, TextAlignmentOptions.Center); summary.rectTransform.sizeDelta = new Vector2(1000, 140);
            Set(shop, "summaryText", summary);
            Set(shop, "buyRationButton", MakeButton(shopPanel.transform, "Buy ration", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(360, 56)));
            Set(shop, "buyBatteryButton", MakeButton(shopPanel.transform, "Buy battery", new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(360, 56)));
            Set(shop, "closeButton", MakeButton(shopPanel.transform, "Back to the boat", new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(360, 56)));
            var stations = vessel.GetComponentsInChildren<SupplyStation>();
            foreach (var s in stations) { var so = new SerializedObject(s); int kind = so.FindProperty("kind").enumValueIndex; Set(shop, kind == 0 ? "rationStation" : "batteryStation", s); }
            Set(shop, "panel", shopPanel); shopPanel.SetActive(false);

            string path = $"{Scenes}/{GameConstants.SceneLoch}.unity";
            EditorSceneManager.SaveScene(scene, path); return path;
        }

        private static string BuildEndingScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            var (canvas, root) = MakeCanvas("EndingCanvas");
            var ui = root.AddComponent<EndingUI>();
            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasGroup)); overlay.transform.SetParent(root.transform, false); Stretch(overlay);
            var title = MakeText(overlay.transform, "Title", "", new Vector2(0.5f, 1f), new Vector2(0, -100), 60f, TextAlignmentOptions.Center); title.rectTransform.sizeDelta = new Vector2(1400, 100);
            var stats = MakeText(overlay.transform, "Stats", "", new Vector2(0.5f, 0.5f), new Vector2(0, 0), 28f, TextAlignmentOptions.Center); stats.rectTransform.sizeDelta = new Vector2(1000, 500);
            var back = MakeButton(overlay.transform, "Return to the docks", new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(400, 56));
            Set(ui, "titleText", title); Set(ui, "statsText", stats); Set(ui, "returnButton", back); Set(ui, "overlay", overlay.GetComponent<CanvasGroup>());
            string path = $"{Scenes}/{GameConstants.SceneEnding}.unity";
            EditorSceneManager.SaveScene(scene, path); return path;
        }
    }
}
