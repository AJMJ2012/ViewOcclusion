using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Light;
using Terraria.ModLoader;
using Terraria.DataStructures;
using System.Threading;

namespace ViewOcclusion {
	public class ViewOcclusion : Mod {

		public static bool enabled = true;
		public static int shadowResolutionPower = 12;
		public static float shadowSmoothness = 10f;
		public static float shadowOpacity = 1f;

		public static bool rebuildRenderTargets = false;
		
		public static int specialDraw = 0;

		//The Alpha channel of the shadowMap corresponds to what can cast shadows
		public RenderTarget2D shadowMap;
		public RenderTarget2D shadowMapHoles1;
		public RenderTarget2D shadowMapHoles2;
		public RenderTarget2D shadowCaster;
		public RenderTarget2D mappedShadows;
		public RenderTarget2D[] shadowReducer;
		public RenderTarget2D lightMap;
		public RenderTarget2D lightMapSwap;
		public RenderTarget2D screen;

		public struct Light {
			public int x;
			public int y;
			public Light(int x, int y) {
				this.x = x;
				this.y = y;
			}
		}

		public static Effect fancyLights;

		public static BlendState lightBlend = new BlendState(){
			ColorBlendFunction = BlendFunction.Max,
			AlphaBlendFunction = BlendFunction.Max,
			ColorSourceBlend = Blend.One,
			AlphaSourceBlend = Blend.One,
			ColorDestinationBlend = Blend.One,
			AlphaDestinationBlend = Blend.One,
			ColorWriteChannels = ColorWriteChannels.All,
		};

		public override void Load() {
			if (!Main.dedServ) {
				fancyLights = ModContent.Request<Effect>("ViewOcclusion/Effects/Lights", (AssetRequestMode)2).Value;
				On.Terraria.Graphics.Effects.FilterManager.EndCapture += FilterManager_EndCapture;
				On.Terraria.Main.LoadWorlds += Main_LoadWorlds;
				On.Terraria.NPC.GetNPCColorTintedByBuffs += NPC_GetNPCColorTintedByBuffs;
				Main.OnResolutionChanged += Main_OnResolutionChanged;
			}
		}

		public override void Unload() {
			if (!Main.dedServ) {
				On.Terraria.Graphics.Effects.FilterManager.EndCapture -= FilterManager_EndCapture;
				On.Terraria.Main.LoadWorlds -= Main_LoadWorlds;
				On.Terraria.NPC.GetNPCColorTintedByBuffs -= NPC_GetNPCColorTintedByBuffs;
				Main.OnResolutionChanged -= Main_OnResolutionChanged;
			}
		}

		public override void PostSetupContent() {
			if (!Main.dedServ) {
				fancyLights = ModContent.Request<Effect>("ViewOcclusion/Effects/Lights", (AssetRequestMode)2).Value;
			}
		}

		internal void DrawSolid(TileDrawing TilesRenderer, bool tileHoles = false) {
			try {
			Vector2 unscaledPosition = Main.Camera.UnscaledPosition;
			Vector2 vector = new Vector2(Main.offScreenRange, Main.offScreenRange);
			if (Main.drawToScreen) {
				vector = Vector2.Zero;
			}
			TilesRenderer.GetType().GetMethod("EnsureWindGridSize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(TilesRenderer, null);
			TilesRenderer.GetType().GetMethod("ClearLegacyCachedDraws", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(TilesRenderer, null);
			TilesRenderer.ClearCachedTileDraws(true);
			GetScreenDrawArea(unscaledPosition, vector + (Main.Camera.UnscaledPosition - Main.Camera.ScaledPosition), out var firstTileX, out var lastTileX, out var firstTileY, out var lastTileY);
			byte b = (byte)(100f + 150f * Main.martianLight);
			TileDrawInfo drawData = new ThreadLocal<TileDrawInfo>(() => new TileDrawInfo()).Value;
			for (int j = firstTileX - 2; j < lastTileX + 2; j++) {
				for (int i = firstTileY; i < lastTileY + 4; i++) {
					Tile tile = Main.tile[j, i];
					if (tile != null && tile.HasTile) {
						ushort type = tile.TileType;
						if (tileHoles) {
							if (!(Main.LocalPlayer.findTreasure && Main.tileSpelunker[type]) && !(Main.LocalPlayer.dangerSense && (bool)typeof(TileDrawing).GetMethod("IsTileDangerous", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[]{j, i, Main.LocalPlayer, tile, type})))
								continue;
						}
						else {
							if ((!Main.tileSolid[type] && !Main.tileSolidTop[type]) || !Main.tileBlockLight[type] || tile.IsActuated)
								continue;
						}
						short frameX = tile.TileFrameX;
						short frameY = tile.TileFrameY;
						TilesRenderer.GetType().GetMethod("DrawSingleTile", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(TilesRenderer, new object[]{drawData, true, -1, unscaledPosition, vector, j, i});
					}
				}
			}
			TilesRenderer.GetType().GetMethod("DrawSpecialTilesLegacy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(TilesRenderer, new object[]{unscaledPosition, vector});
			}
			catch {}
		}

		internal void GetScreenDrawArea(Vector2 screenPosition, Vector2 offSet, out int firstTileX, out int lastTileX, out int firstTileY, out int lastTileY) {
			firstTileX = (int)((screenPosition.X - offSet.X) / 16f - 1f);
			lastTileX = (int)((screenPosition.X + (float)Main.screenWidth + offSet.X) / 16f) + 2;
			firstTileY = (int)((screenPosition.Y - offSet.Y) / 16f - 1f);
			lastTileY = (int)((screenPosition.Y + (float)Main.screenHeight + offSet.Y) / 16f) + 5;
			if (firstTileX < 4) {
				firstTileX = 4;
			}
			if (lastTileX > Main.maxTilesX - 4) {
				lastTileX = Main.maxTilesX - 4;
			}
			if (firstTileY < 4) {
				firstTileY = 4;
			}
			if (lastTileY > Main.maxTilesY - 4) {
				lastTileY = Main.maxTilesY - 4;
			}
		}

		internal void RebuildRenderTargets() {
			GraphicsDevice graphicsDevice = Main.instance.GraphicsDevice;
			screen = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
			lightMap = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
			lightMapSwap = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
			shadowMap = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
			shadowMapHoles1 = new RenderTarget2D(graphicsDevice, Main.screenWidth + Main.offScreenRange, Main.screenHeight + Main.offScreenRange);
			shadowMapHoles2 = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
			shadowCaster = new RenderTarget2D(graphicsDevice, 1 << shadowResolutionPower, 1 << shadowResolutionPower);
			mappedShadows = new RenderTarget2D(graphicsDevice, 1 << shadowResolutionPower, 1 << shadowResolutionPower);
			shadowReducer = new RenderTarget2D[shadowResolutionPower - 1];
			for (int i = 1; i < shadowResolutionPower; i++) {
				shadowReducer[i - 1] = new RenderTarget2D(graphicsDevice, 1 << i, 1 << shadowResolutionPower);
			}
	
			/*screen = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			lightMap = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			lightMapSwap = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			shadowMap = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			shadowMapHoles1 = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth + Main.offScreenRange, graphicsDevice.PresentationParameters.BackBufferHeight + Main.offScreenRange, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			shadowMapHoles2 = new RenderTarget2D(graphicsDevice, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			shadowCaster = new RenderTarget2D(graphicsDevice, 1 << shadowResolutionPower, 1 << shadowResolutionPower, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			mappedShadows = new RenderTarget2D(graphicsDevice, 1 << shadowResolutionPower, 1 << shadowResolutionPower, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			shadowReducer = new RenderTarget2D[shadowResolutionPower - 1];
			for(int i = 1; i < shadowResolutionPower; i++) {
				shadowReducer[i - 1] = new RenderTarget2D(graphicsDevice, 1 << i, 1 << shadowResolutionPower, false, graphicsDevice.PresentationParameters.BackBufferFormat, (DepthFormat)0);
			}*/

			rebuildRenderTargets = false;
		}

		private Color NPC_GetNPCColorTintedByBuffs(On.Terraria.NPC.orig_GetNPCColorTintedByBuffs orig, NPC npc, Color npcColor) {
			npcColor = orig(npc, npcColor);
			if (Main.player[Main.myPlayer].detectCreature && npc.lifeMax > 1) {
				if (npc.friendly || npc.catchItem > 0 || (npc.damage == 0 && npc.lifeMax == 5)) {
					npcColor.R = (byte)Math.Min(Math.Max(npcColor.R * 2, 127), 255);
					npcColor.G = byte.MaxValue;
					npcColor.B = (byte)Math.Min(Math.Max(npcColor.B * 2, 127), 255);
				}
				else {
					npcColor.R = byte.MaxValue;
					npcColor.G = (byte)Math.Min(Math.Max(npcColor.G * 2, 127), 255);
					npcColor.B = (byte)Math.Min(Math.Max(npcColor.B * 2, 127), 255);
				}
			}
			return npcColor;
		}

		private void Main_LoadWorlds(On.Terraria.Main.orig_LoadWorlds orig) {
			orig();
			if (screen == null)
				RebuildRenderTargets();
		}

		private void Main_OnResolutionChanged(Vector2 obj) {
			RebuildRenderTargets();
		}

		private void FilterManager_EndCapture(On.Terraria.Graphics.Effects.FilterManager.orig_EndCapture orig, FilterManager self, RenderTarget2D finalTexture, RenderTarget2D screenTarget1, RenderTarget2D screenTarget2, Color clearColor) {
			GraphicsDevice graphicsDevice = Main.instance.GraphicsDevice;
			SpriteBatch spriteBatch = Main.spriteBatch;
			TileDrawing tilesRenderer = Main.instance.TilesRenderer;
			LightingEngine lightingEngine = typeof(Lighting).GetField("_activeEngine", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as LightingEngine;
			try {
				if (rebuildRenderTargets)
					RebuildRenderTargets();

				if (enabled && lightingEngine != null) {

					//Save Swap
					graphicsDevice.SetRenderTarget(Main.screenTargetSwap);
					graphicsDevice.Clear(Color.Transparent);
					spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
					spriteBatch.Draw(Main.screenTarget, Vector2.Zero, Color.White);
					spriteBatch.End();

					//Draw Tile Shadowmap
					graphicsDevice.SetRenderTarget(shadowMap);
					graphicsDevice.Clear(Color.Transparent);
					spriteBatch.Begin();
					DrawSolid(tilesRenderer);
					spriteBatch.End();

					//Draw Tile Shadowmap Holes
					graphicsDevice.SetRenderTarget(shadowMapHoles1);
					graphicsDevice.Clear(Color.Transparent);
					if (Main.LocalPlayer.findTreasure || Main.LocalPlayer.dangerSense) {
						spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
						DrawSolid(tilesRenderer, true);
						spriteBatch.End();
					}

					//Draw Tile Shadowmap Holes
					graphicsDevice.SetRenderTarget(shadowMapHoles2);
					graphicsDevice.Clear(Color.Transparent);
					if (Main.LocalPlayer.detectCreature) {
						spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
						Main.instance.GetType().GetMethod("DrawNPCs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Main.instance, new object[]{false});
						Main.instance.GetType().GetMethod("DrawNPCs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Main.instance, new object[]{true});
						spriteBatch.End();
					}

					//Do PerLightShading
					graphicsDevice.SetRenderTarget(lightMapSwap);
					graphicsDevice.Clear(new Color(0, 0, 0, 0));
					graphicsDevice.SetRenderTarget(lightMap);
					graphicsDevice.Clear(new Color(0, 0, 0, 0));

					Player player = Main.player[Main.myPlayer];
					PerLightShading(ref graphicsDevice, ref spriteBatch, lightBlend, new Light((int)player.Center.X, (int)(player.Center.Y - (player.height / 4f))));

					//Render Swap and Screen to Final
					graphicsDevice.SetRenderTarget(Main.screenTarget);
					graphicsDevice.Clear(Color.Transparent);
					spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);

					fancyLights.Parameters["darkBrightness"].SetValue(shadowOpacity);
					fancyLights.Parameters["brightBrightness"].SetValue(2f);
					fancyLights.Parameters["brightnessGrowthBase"].SetValue(2f);
					fancyLights.Parameters["brightnessGrowthRate"].SetValue(2f);
					fancyLights.Parameters["blurDistance"].SetValue(new Vector2(16f * (1.0f/5.0f) * shadowSmoothness / (float)Main.screenWidth, 16f * (1.0f / 5.0f) * shadowSmoothness / (float)Main.screenHeight));
					fancyLights.Parameters["lightMapTexture"].SetValue(lightMap);
					fancyLights.CurrentTechnique.Passes["CompositeFinal"].Apply();
					spriteBatch.Draw(Main.screenTargetSwap, Vector2.Zero, Color.White);
					spriteBatch.End();
				}
			}
			catch {
				try { spriteBatch.End(); }
				catch {}
			}
			orig(self, finalTexture, screenTarget1, screenTarget2, clearColor);
		}

		private void PerLightShading(ref GraphicsDevice graphicsDevice, ref SpriteBatch spriteBatch, BlendState lightBlend, Light light, bool isBlock = false) {
			try {
				int lightDistance = (int)(Math.Sqrt(Math.Pow(Main.screenWidth / Main.GameViewMatrix.Zoom.X, 2) + Math.Pow(Main.screenHeight / Main.GameViewMatrix.Zoom.Y, 2)) / 2f);
				
				graphicsDevice.SetRenderTarget(shadowCaster);
				graphicsDevice.Clear(Color.White);
				spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
				fancyLights.Parameters["lightCenter"].SetValue(new Vector2((float)((int)light.x - Main.screenPosition.X + Main.offScreenRange) / (float) ((Main.screenWidth)), (float)(light.y - (int)Main.screenPosition.Y + Main.offScreenRange) / (float) ((Main.screenHeight ))) );
				fancyLights.Parameters["sizeMult"].SetValue(new Vector2((float)((Main.screenWidth)) / (float) (lightDistance), (float)((Main.screenHeight)) / (float)(lightDistance)));
				fancyLights.Parameters["sizeBlock"].SetValue(isBlock ? new Vector2(8.0f / (float)lightDistance, 8.0f / (float)lightDistance) : new Vector2(-1, -1));
				fancyLights.CurrentTechnique.Passes["DistanceToShadowcaster"].Apply();
				spriteBatch.Draw(shadowMap, new Rectangle(0, 0, 1 << shadowResolutionPower, 1 << shadowResolutionPower), new Rectangle(light.x - (int) Main.screenPosition.X + (int)(Main.offScreenRange ) - lightDistance, light.y - (int) Main.screenPosition.Y + (int)(Main.offScreenRange ) - lightDistance, (int)(lightDistance * 2), (int)(lightDistance * 2)), Color.White);
				spriteBatch.End();

				graphicsDevice.SetRenderTarget(mappedShadows);
				graphicsDevice.Clear(Color.White);
				spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
				fancyLights.CurrentTechnique.Passes["DistortEquidistantAngle"].Apply();
				spriteBatch.Draw(shadowCaster, Vector2.Zero, Color.White);
				spriteBatch.End();

				int step = shadowResolutionPower - 2;
				while (step >= 0) {
					RenderTarget2D d = shadowReducer[step];
					RenderTarget2D s = (step == shadowResolutionPower - 2) ? mappedShadows : shadowReducer[step + 1];
					graphicsDevice.SetRenderTarget(d);
					graphicsDevice.Clear(Color.White);
					spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
					fancyLights.Parameters["texWidth"].SetValue(1.0f / ((float)(s.Width)));
					fancyLights.CurrentTechnique.Passes["HorizontalReduce"].Apply();
					spriteBatch.Draw(s, Vector2.Zero, Color.White);
					spriteBatch.End();

					step--;
				}

				graphicsDevice.SetRenderTarget(lightMap);
				graphicsDevice.Clear(new Color(0, 0, 0, 0));
				spriteBatch.Begin(SpriteSortMode.Immediate, lightBlend);
				spriteBatch.Draw(lightMapSwap, Vector2.Zero, Color.White);
				spriteBatch.Draw(shadowMapHoles1, new Vector2(shadowMapHoles1.Width + (Main.offScreenRange * (Main.GameViewMatrix.Zoom.X - 1)), shadowMapHoles1.Height + (Main.offScreenRange * (Main.GameViewMatrix.Zoom.Y - 1))) / 2f, new Rectangle(Main.offScreenRange, Main.offScreenRange, shadowMapHoles1.Width, shadowMapHoles1.Height), Color.White, 0, new Vector2(shadowMapHoles1.Width, shadowMapHoles1.Height) / 2f, Main.GameViewMatrix.Zoom, SpriteEffects.None, 0);
				spriteBatch.Draw(shadowMapHoles2, new Vector2(shadowMapHoles2.Width, shadowMapHoles2.Height) / 2f, new Rectangle(0, 0, shadowMapHoles2.Width, shadowMapHoles2.Height), Color.White, 0, new Vector2(shadowMapHoles2.Width, shadowMapHoles2.Height) / 2f, Main.GameViewMatrix.Zoom, SpriteEffects.None, 0);
				fancyLights.Parameters["lightColor"].SetValue(new Vector3(1f, 1f, 1f));
				fancyLights.Parameters["shadowMapTexture"].SetValue(shadowReducer[0]);
				fancyLights.CurrentTechnique.Passes["ApplyShadow"].Apply();
				spriteBatch.Draw(shadowCaster, new Rectangle((int)((light.x - Main.screenPosition.X - lightDistance) * Main.GameViewMatrix.Zoom.X - Main.screenWidth * (Main.GameViewMatrix.Zoom.X - 1) * 0.5), (int)((light.y - Main.screenPosition.Y - lightDistance) * Main.GameViewMatrix.Zoom.Y - Main.screenHeight * (Main.GameViewMatrix.Zoom.Y - 1) * 0.5), (int)(lightDistance * 2 * Main.GameViewMatrix.Zoom.Y), (int)(lightDistance * 2 * Main.GameViewMatrix.Zoom.Y)), Color.White);
				spriteBatch.End();

				graphicsDevice.SetRenderTarget(lightMapSwap);
				graphicsDevice.Clear(new Color(0,0,0,0));
				spriteBatch.Begin(SpriteSortMode.Immediate, lightBlend);
				spriteBatch.Draw(lightMap, Vector2.Zero, Color.White);
				spriteBatch.End();

				return;
			}
			catch {
				try { spriteBatch.End(); }
				catch {}
			}
		}
	}
}