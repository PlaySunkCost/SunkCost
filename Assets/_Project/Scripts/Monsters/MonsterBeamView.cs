using UnityEngine;

namespace SunkCost.Monsters
{
    // A beam as every peer draws it (Dan, 22 September 2026: a second of loading,
    // then three seconds of beam), exactly where the server judges it (24 September
    // 2026): from the creature's beam origin (CreatureBolts.Origin, the model's
    // mouth or lamp) to the replicated end, as thick as CreatureBolts.HalfWidth —
    // what is drawn is what hurts. While it charges a thread points where it aims
    // and the mouth gathers; while it burns a core and a sheath the width of the
    // hit, an impact where it meets a wall, a flare when the hit lands; then it
    // fades. The host draws the server's line as it is; a guest eases after the
    // replicated end between ticks.
    //
    // The two lasers are meant to differ (Dan's two shooters): the Lure's LIGHT beam
    // is a bright white-gold core in a warm halo, shimmering fast, and its charge
    // swells outward like a lamp coming up; the Listener's DARK beam is a black
    // core in a violet sheath, throbbing slowly and unevenly, and its charge
    // gathers inward — a wide violet haze collapsing into a dense black knot at the
    // mouth. Big and heavy (Dan, 24 September 2026: "much bigger"): everything —
    // the sheath, the halo, the charge's swell and haze, the start flare, the wall's
    // glow — scales with the beam's width, and in the charge's last third a faint
    // shell the beam's full width shows how big it will be. The glows at the mouth
    // are capped (a disc wider than the head hid its body language, mon-lure) and
    // fall off from the centre in nested shells. Presentation only.
    public sealed class MonsterBeamView : MonoBehaviour
    {
        private const float FadeSeconds = 0.3f, AimEaseSeconds = 0.03f, HitFlareSeconds = 0.25f, FireFlashSeconds = 0.12f;
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static Material additive, blended;
        private LineRenderer core, sheath, ghost;
        private Transform knot, haze, impact;
        private Renderer knotRenderer;
        private Renderer[] hazeShells, impactShells; // soft glows: nested shells, brightest at the centre, seen alike from every camera
        private MaterialPropertyBlock block;
        private CreatureBolts bolts;
        private bool dark;
        private BeamPhase phase = BeamPhase.None;
        private float phaseAt = float.NegativeInfinity, hitAt = float.NegativeInfinity;
        private Vector3 shownAim;
        private bool aimPrimed;

        public BeamPhase Phase => phase;
        public bool Dark => dark;
        // Exactly what is drawn this frame (mon-lure's lantern light and landing glow follow it; the checks read it).
        public Vector3 ShownFrom { get; private set; }
        public Vector3 ShownTo => shownAim;
        public float ShownHalfWidth { get; private set; }
        public bool ShownImpact => impact != null && impact.gameObject.activeSelf;

        private static readonly Color DarkViolet = new(0.45f, 0.15f, 0.75f), DarkCore = new(0.035f, 0.0f, 0.06f);
        private static readonly Color LightGold = new(1f, 0.92f, 0.55f), LightCore = new(1f, 0.98f, 0.9f);

        public static MonsterBeamView Make(Transform creature, CreatureBolts bolts)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(creature, false);
            MonsterBeamView view = go.AddComponent<MonsterBeamView>();
            view.bolts = bolts;
            view.block = new MaterialPropertyBlock();
            view.ghost = Line(go.transform, "Ghost");
            view.sheath = Line(go.transform, "Sheath");
            view.core = Line(go.transform, "Core");
            view.knot = Ball(go.transform, "Charge knot", out view.knotRenderer);
            view.haze = Soft(go.transform, "Charge haze", out view.hazeShells);
            view.impact = Soft(go.transform, "Impact", out view.impactShells);
            if (bolts != null) bolts.Hit += view.OnHit;
            return view;
        }

        private static LineRenderer Line(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private static Transform Ball(Transform parent, string name, out Renderer renderer)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = name;
            Object.Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(parent, false);
            renderer = ball.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ball.SetActive(false);
            return ball.transform;
        }

        // A soft glow: three nested additive spheres (whole, 70 %, 45 % of the size), so
        // the light falls off from the centre instead of reading as a flat disc — the
        // same from a diver's camera, a spectator's and the deck TV's.
        private static readonly float[] ShellScale = { 1f, 0.7f, 0.45f }, ShellWeight = { 0.22f, 0.3f, 0.48f };
        private static Transform Soft(Transform parent, string name, out Renderer[] shells)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            shells = new Renderer[ShellScale.Length];
            for (int i = 0; i < ShellScale.Length; i++)
            {
                Transform shell = Ball(root, name + " " + i, out shells[i]);
                shell.localScale = Vector3.one * ShellScale[i];
                shell.gameObject.SetActive(true);
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private void SoftGlow(Renderer[] shells, Color c, float k)
        {
            for (int i = 0; i < shells.Length; i++) Glow(shells[i], c, k * ShellWeight[i] * 2.2f);
        }

        private void OnDestroy() { if (bolts != null) bolts.Hit -= OnHit; }
        private void OnHit() => hitAt = Time.time;

        private Vector3 Origin => bolts != null ? bolts.Origin : transform.position;
        private float HalfWidth => bolts != null ? bolts.HalfWidth : 0.35f;

        private void Dress(bool isDark)
        {
            dark = isDark;
            // The light beam is all glow (additive); the dark one's core and knot are a
            // darkness laid over the scene (blended), inside an additive violet sheath.
            sheath.sharedMaterial = Additive();
            ghost.sharedMaterial = Additive();
            core.sharedMaterial = dark ? Blended() : Additive();
            knotRenderer.sharedMaterial = dark ? Blended() : Additive();
            foreach (Renderer r in hazeShells) r.sharedMaterial = Additive();
            foreach (Renderer r in impactShells) r.sharedMaterial = Additive();
        }

        public void Charge(BeamCue cue)
        {
            Dress(cue.Dark);
            phase = BeamPhase.Charging;
            phaseAt = Time.time;
            shownAim = cue.To;
            aimPrimed = true;
            Show(true);
        }

        public void Fire(BeamCue cue)
        {
            Dress(cue.Dark);
            phase = BeamPhase.Firing;
            phaseAt = Time.time;
            if (!aimPrimed) { shownAim = cue.To; aimPrimed = true; }
            Show(true);
        }

        public void End()
        {
            phase = BeamPhase.Done;
            phaseAt = Time.time;
        }

        private void Show(bool on)
        {
            core.enabled = sheath.enabled = on;
            if (!on) ghost.enabled = false;
            knot.gameObject.SetActive(on);
            haze.gameObject.SetActive(on);
            if (!on) impact.gameObject.SetActive(false);
        }

        private void Paint(Renderer r, Color c)
        {
            block.SetColor(BaseColourId, c);
            r.SetPropertyBlock(block);
        }

        // An additive glow: the colour scaled, alpha whole (Color * k would scale the alpha too).
        private void Glow(Renderer r, Color c, float k) => Paint(r, new Color(c.r * k, c.g * k, c.b * k, 1f));

        private void Width(LineRenderer line, float half) => line.startWidth = line.endWidth = Mathf.Max(0f, half * 2f);

        private void LateUpdate()
        {
            if (phase == BeamPhase.None) return;
            float now = Time.time, since = now - phaseAt;
            Vector3 from = Origin;
            // The host draws the server's own line; a guest eases after the replicated end between ticks.
            Vector3 target = bolts != null ? bolts.BeamAim : shownAim;
            bool server = bolts != null && bolts.IsServerStarted;
            shownAim = server || !aimPrimed ? target : Vector3.Lerp(shownAim, target, 1f - Mathf.Exp(-Time.deltaTime / AimEaseSeconds));
            ShownFrom = from;
            core.SetPosition(0, from); core.SetPosition(1, shownAim);
            sheath.SetPosition(0, from); sheath.SetPosition(1, shownAim);
            ghost.SetPosition(0, from); ghost.SetPosition(1, shownAim);
            knot.position = haze.position = from;
            float half = HalfWidth;
            float range = MonsterSettings.Get().BeamRangeMeters;
            bool onWall = (shownAim - from).magnitude < range - 0.05f;
            Color glow = dark ? DarkViolet : LightGold;
            Color inner = dark ? DarkCore : LightCore;
            // The dark beam throbs slowly and unevenly; the light one shimmers fast and even.
            float throb = dark ? 0.7f + 0.3f * Mathf.PerlinNoise(now * 3.1f, 0.37f) + 0.08f * Mathf.Sin(now * 11f) : 0.88f + 0.12f * Mathf.Sin(now * 60f);
            switch (phase)
            {
                case BeamPhase.Charging:
                {
                    float charge = MonsterSettings.Get().BeamChargeSeconds;
                    float t = charge <= 0f ? 1f : Mathf.Clamp01(since / charge);
                    // The thread: a line that firms up where it aims; the last tenth of a second flickers — the tell.
                    float tell = t > 0.88f ? 0.6f + 0.4f * Mathf.Sin(now * 70f) : 1f;
                    float thread = Mathf.Lerp(0.012f, 0.1f * half + 0.01f, t);
                    Width(sheath, thread); Width(core, thread * 0.5f);
                    Glow(sheath, glow, (0.35f + 0.9f * t) * throb * tell * (dark ? 1.4f : 1f));
                    if (dark) Paint(core, new Color(inner.r, inner.g, inner.b, 0.4f + 0.5f * t)); else Glow(core, inner, 0.3f + 0.7f * t);
                    // The ghost: in the charge's last third a faint shell the beam's full width shows how big it will be.
                    float ghostOn = Mathf.Clamp01((t - 0.62f) / 0.3f);
                    ghost.enabled = ghostOn > 0f;
                    Width(ghost, half);
                    Glow(ghost, glow, ghostOn * (dark ? 0.22f : 0.16f) * tell);
                    if (dark)
                    {
                        // Gathering in: a wide haze collapses while the black knot at the mouth grows dense.
                        haze.localScale = Vector3.one * Mathf.Lerp(Mathf.Min(Mathf.Max(1.2f, half * 6f), 1.4f), Mathf.Min(half * 2.2f, 0.6f), t);
                        SoftGlow(hazeShells, glow, (0.15f + 0.85f * t * t) * throb);
                        knot.localScale = Vector3.one * Mathf.Lerp(0.03f, Mathf.Min(half * 1.8f, 0.32f), t);
                        Paint(knotRenderer, new Color(inner.r, inner.g, inner.b, 0.6f + 0.35f * t));
                    }
                    else
                    {
                        // Coming up like a lamp: the glow swells outward from the mouth.
                        knot.localScale = Vector3.one * Mathf.Lerp(0.06f, Mathf.Min(half * 3.2f, 0.5f), t * t);
                        Glow(knotRenderer, inner, (0.5f + 1.5f * t) * throb);
                        haze.localScale = Vector3.one * Mathf.Lerp(0.1f, Mathf.Min(half * 5f, 0.9f), t * t);
                        SoftGlow(hazeShells, glow, 0.1f + 0.5f * t * t);
                    }
                    impact.gameObject.SetActive(false);
                    ShownHalfWidth = thread;
                    break;
                }
                case BeamPhase.Firing:
                {
                    // Exactly the hit's width while it burns; a flash as it lights, a flare when it lands.
                    float flash = since < FireFlashSeconds ? 1f + 0.6f * (1f - since / FireFlashSeconds) : 1f; // the start flare at the mouth
                    ghost.enabled = false;
                    float flare = now - hitAt < HitFlareSeconds ? 1f + 1.5f * (1f - (now - hitAt) / HitFlareSeconds) : 1f;
                    float lift = flash * flare * throb;
                    Width(sheath, half); Width(core, half * (dark ? 0.5f : 0.38f));
                    Glow(sheath, glow, (dark ? 1.6f : 1.1f) * lift);
                    if (dark) Paint(core, new Color(inner.r, inner.g, inner.b, 0.92f)); else Glow(core, inner, 1.6f * lift);
                    knot.localScale = Vector3.one * (dark ? Mathf.Min(half * 1.9f, 0.34f) : Mathf.Min(half * 2.4f, 0.55f)) * flash;
                    if (dark) Paint(knotRenderer, new Color(inner.r, inner.g, inner.b, 0.95f)); else Glow(knotRenderer, inner, 1.8f * lift);
                    haze.localScale = Vector3.one * (dark ? Mathf.Min(half * 3.2f, 0.9f) : Mathf.Min(half * 4f, 1.0f)) * flash;
                    SoftGlow(hazeShells, glow, 0.6f * lift);
                    impact.gameObject.SetActive(onWall);
                    if (onWall)
                    {
                        impact.position = shownAim;
                        impact.localScale = Vector3.one * Mathf.Min(half * (dark ? 3.4f : 3f), 1.2f) * (0.9f + 0.2f * throb);
                        SoftGlow(impactShells, glow, (dark ? 1.4f : 1.8f) * lift);
                    }
                    ShownHalfWidth = half;
                    break;
                }
                case BeamPhase.Done:
                {
                    float fade = 1f - Mathf.Clamp01(since / FadeSeconds);
                    ghost.enabled = false;
                    if (fade <= 0f) { Show(false); phase = BeamPhase.None; aimPrimed = false; ShownHalfWidth = 0f; return; }
                    // The dark beam thins from the outside in, the light one dims.
                    Width(sheath, half * (dark ? fade * fade : fade)); Width(core, half * 0.45f * fade);
                    Glow(sheath, glow, fade);
                    if (dark) Paint(core, new Color(inner.r, inner.g, inner.b, 0.9f * fade)); else Glow(core, inner, fade);
                    knot.localScale = Vector3.one * Mathf.Min(half * 1.8f, dark ? 0.34f : 0.5f) * fade;
                    if (dark) Paint(knotRenderer, new Color(inner.r, inner.g, inner.b, 0.9f * fade)); else Glow(knotRenderer, inner, fade);
                    haze.localScale = Vector3.one * Mathf.Min(half * 3f, 0.9f) * fade;
                    SoftGlow(hazeShells, glow, 0.5f * fade);
                    impact.gameObject.SetActive(onWall && fade > 0.3f);
                    if (onWall) { impact.position = shownAim; SoftGlow(impactShells, glow, fade); }
                    ShownHalfWidth = half * fade;
                    break;
                }
            }
        }

        // URP Unlit made transparent by hand (the dash rings' recipe): an additive glow, and a blended darkness.
        private static Material Additive() => additive != null ? additive : additive = Transparent("Beam glow", UnityEngine.Rendering.BlendMode.One);
        private static Material Blended() => blended != null ? blended : blended = Transparent("Beam dark", UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        private static Material Transparent(string name, UnityEngine.Rendering.BlendMode destination)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material m = new(shader != null ? shader : Shader.Find("Sprites/Default")) { name = name };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)destination);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (destination == UnityEngine.Rendering.BlendMode.One ? 0 : 1); // the darkness over the glow: a black core inside the violet
            m.SetColor(BaseColourId, Color.white);
            return m;
        }
    }
}
