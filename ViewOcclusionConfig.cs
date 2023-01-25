using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.Config.UI;

namespace ViewOcclusion {
    class ViewOcclusionConfig : ModConfig {
        [Label("Enabled")]
        [DefaultValue(true)]
        public bool Enabled;

        [Label("Occlusion Amount")]
        [Range(0.0f, 1.0f)]
        [DefaultValue(1f)]
        [Slider]
        public float OcclusionAmount;

        [Label("Occlusion Smoothness")]
        [Range(0.0f, 20.0f)]
        [DefaultValue(10f)]
        [Slider]
        public float OcclusionSmooth;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        public override void OnChanged() {
            ViewOcclusion.enabled = Enabled && OcclusionAmount > 0;
            ViewOcclusion.shadowSmoothness = OcclusionSmooth;
            ViewOcclusion.shadowOpacity = 1f - OcclusionAmount;
            ViewOcclusion.rebuildRenderTargets = true;
        }
    }
}
