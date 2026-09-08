using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarturHDBlood
{
    /// Random splat shapes without relying on Texture Sheet Animation.
    ///
    /// The sheet-animation approach worked for the airborne spray but not for the ground decals:
    /// "Custom/ParticleDecal" doesn't honour the module's UV remapping, so every decal sampled
    /// the whole atlas and every puddle came out identically. Nothing in the shader is
    /// configurable from here, so the variety has to come from somewhere the shader can't ignore.
    ///
    /// So: cut the atlas into its cells as separate textures, clone the decal material once per
    /// cell, and hand a randomly chosen clone to each decal as it spawns. The shader only ever
    /// sees a plain single-image material, which it cannot mishandle.
    ///
    /// Cost is bounded and small - one material and one texture per cell, eight of each, built
    /// once. Assignment per spawn is a single reference write.
    internal static class Variants
    {
        private static Material[] _large;
        private static Material[] _small;
        private static bool _built;

        private static readonly System.Random Rng = new System.Random();

        /// Cells are indexed in reading order, so with SizeAwareVariants on, the top row is the
        /// large set and the bottom row the small set - the same split the atlas is authored to.
        public static void Build(Material template, Texture2D atlas, Texture2D atlasNormal)
        {
            if (_built || template == null || atlas == null)
                return;
            _built = true;

            int cols = Mathf.Max(1, Plugin.AtlasColumns.Value);
            int rows = Mathf.Max(1, Plugin.AtlasRows.Value);
            // Derived from the texture, not trusted from config - see BloodSkin.ResolveGrid.
            BloodSkin.ResolveGrid(atlas, Plugin.AtlasCellSize.Value, ref cols, ref rows, "Splat atlas");

            int cellW = atlas.width / cols;
            int cellH = atlas.height / rows;

            var large = new List<Material>();
            var small = new List<Material>();

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Material m = MakeVariant(template, atlas, atlasNormal, col, row, cellW, cellH);
                    if (m == null)
                        continue;

                    bool smallRow = Plugin.SizeAwareVariants.Value && rows > 1 && row == rows - 1;
                    (smallRow ? small : large).Add(m);
                }
            }

            // With size-awareness off, or a single-row sheet, every cell is a candidate for both.
            if (small.Count == 0)
                small.AddRange(large);
            if (large.Count == 0)
                large.AddRange(small);

            _large = large.ToArray();
            _small = small.ToArray();

            Plugin.Log.LogInfo($"Built {_large.Length} large and {_small.Length} small decal " +
                               $"variant material(s) from the {cols}x{rows} atlas.");
        }

        private static Material MakeVariant(Material template, Texture2D atlas,
                                            Texture2D atlasNormal,
                                            int col, int row, int cellW, int cellH)
        {
            try
            {
                // GetPixels works because the atlas was created through LoadImage, which leaves
                // the texture readable.
                Color[] pixels = atlas.GetPixels(col * cellW, row * cellH, cellW, cellH);

                var cell = new Texture2D(cellW, cellH, TextureFormat.RGBA32, true)
                {
                    name = $"CarturBloodCell_{col}_{row}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                cell.SetPixels(pixels);
                cell.Apply(true);

                var mat = new Material(template) { name = $"CarturBloodDecal_{col}_{row}" };
                mat.SetTexture("_MainTex", cell);

                // The normal map has to be sliced to the same cell. Leaving the full atlas
                // normal on a material whose albedo is one cell would light the splat by the
                // wrong part of the sheet - the lighting would simply not match the shape.
                if (atlasNormal != null && mat.HasProperty("_NormalTex")
                                        && atlasNormal.width == atlas.width
                                        && atlasNormal.height == atlas.height)
                {
                    Color[] nPixels = atlasNormal.GetPixels(col * cellW, row * cellH, cellW, cellH);
                    var nCell = new Texture2D(cellW, cellH, TextureFormat.RGBA32, true)
                    {
                        name = $"CarturBloodCellN_{col}_{row}",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                    nCell.SetPixels(nPixels);
                    nCell.Apply(true);
                    mat.SetTexture("_NormalTex", nCell);
                }

                return mat;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not build variant cell {col},{row}: {e.Message}");
                return null;
            }
        }

        /// Assigns a random variant to this decal's renderer. Returns false if variants aren't
        /// available, so the caller can fall back to the shared-material path.
        public static bool Assign(ParticleSystem decalSystem, bool small)
        {
            if (!_built)
                return false;

            Material[] pool = small ? _small : _large;
            if (pool == null || pool.Length == 0)
                return false;

            var r = decalSystem.GetComponent<ParticleSystemRenderer>();
            if (r == null)
                return false;

            // Set on this renderer only. The template material is never mutated, so unrelated
            // users of it are unaffected.
            r.sharedMaterial = pool[Rng.Next(pool.Length)];
            return true;
        }
    }
}
