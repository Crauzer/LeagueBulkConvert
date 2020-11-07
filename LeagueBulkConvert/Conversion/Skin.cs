using ImageMagick;
using LeagueBulkConvert.ViewModels;
using LeagueToolkit.IO.AnimationFile;
using LeagueToolkit.IO.PropertyBin;
using LeagueToolkit.IO.PropertyBin.Properties;
using LeagueToolkit.IO.SimpleSkinFile;
using LeagueToolkit.IO.SkeletonFile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LeagueBulkConvert.Conversion
{
    class Skin
    {
        public List<(string, Animation)> Animations { get; set; }

        public string Character { get; set; }

        public bool Exists { get => File.Exists(Mesh); }

        public bool HasIdle { get; set; } = false;

        public ulong MaterialHash { get; set; }

        public IList<Material> Materials { get; set; } = new List<Material>();

        public string Mesh { get; set; }

        public string Name { get; set; }

        public IList<string> RemoveMeshes { get; set; }

        public string Skeleton { get; set; }

        public string Texture { get; set; }

        public void AddAnimations(string binPath, LoggingWindowViewModel viewModel)
        {
            if (HasIdle)
            {

            }
            var binTree = new BinTree(binPath);
            var animationsData = binTree.Objects.FirstOrDefault(o => o.MetaClassHash == 4126869447); //AnimationGraphData
            if (animationsData == null)
                return;
            var clipData = (BinTreeMap)animationsData.Properties.FirstOrDefault(p => p.NameHash == 1172382456); //mClipDataMap
            if (clipData == null)
                return;
            foreach (var keyValuePair in clipData.Map)
            {
                var hash = ((BinTreeHash)keyValuePair.Key).Value;
                if (!Converter.HashTables["binhashes"].ContainsKey(hash))
                    continue;
                var name = Converter.HashTables["binhashes"][hash];
                var lowercase = name.ToLower();
                if (!(lowercase == "idle1" || lowercase == "idle01" || lowercase == "idle_base" || lowercase == "idle01_base"))
                    continue;
                if (!TryGetAnimationData(clipData, (BinTreeStructure)keyValuePair.Value, out var animationData))
                    continue;
                var pathProperty = (BinTreeString)animationData.Properties.FirstOrDefault(p => p.NameHash == 53080535); //mAnimationFilePath
                if (pathProperty == null)
                    continue;
                var path = pathProperty.Value.ToString().ToLower().Replace('/', '\\');
                if (!File.Exists(path))
                    continue;
                try
                {
                    Animations.Add((name, new Animation(path)));
                    HasIdle = true;
                }
                catch (Exception)
                {
                    viewModel.AddLine($"Couldn't parse {path}", 2);
                }
                break;
            }
        }

        public void Clean()
        {
            var baseMaterial = Materials.FirstOrDefault(m => m.Hash == MaterialHash);
            if (baseMaterial != null && Texture != null && !baseMaterial.IsComplete)
                Materials[Materials.IndexOf(baseMaterial)].Texture = Texture;
            for (var i = 0; i < Materials.Count; i++)
            {
                var material = Materials[i];
                if (material.Hash == 0 && string.IsNullOrWhiteSpace(material.Texture) && !RemoveMeshes.Contains(material.Name))
                    Materials[i].Texture = Texture;
                if ((string.IsNullOrWhiteSpace(material.Texture)
                     || material.Texture.Contains("empty32.dds"))
                     && !RemoveMeshes.Contains(material.Name))
                    RemoveMeshes.Add(material.Name);
                if (!RemoveMeshes.Contains(material.Name))
                    continue;
                Materials.RemoveAt(i);
                i--;
            }
        }

        private bool TryGetAnimationData(BinTreeMap map, BinTreeStructure structure, out BinTreeEmbedded animationData)
        {
            animationData = null;
            switch (structure.MetaClassHash)
            {
                // Ideally, the animations are randomly played with the correct chances, but that isn't possible in glTF.
                // It might be possible to randomise the order and join them together into a single animations, but that
                // is not something I'm able to do.
                case 1240774858: //SelectorClipData
                    var selectorList = (BinTreeContainer)structure.Properties.FirstOrDefault(p => p.NameHash == 1361876261); //mSelectorPairDataList
                    BinTreeEmbedded bestSelector = null;
                    var highestProbability = 0f;
                    foreach (BinTreeEmbedded selector in selectorList.Properties)
                    {
                        var probability = (BinTreeFloat)selector.Properties.FirstOrDefault(p => p.NameHash == 1674287183); //mProbability
                        if (probability == null)
                            continue;
                        if (probability.Value > highestProbability)
                        {
                            bestSelector = selector;
                            highestProbability = probability.Value;
                        }
                    }
                    if (bestSelector == null)
                        break;
                    var clipName = (BinTreeHash)bestSelector.Properties.FirstOrDefault(p => p.NameHash == 3391849597); //mClipName
                    if (clipName == null)
                        break;
                    KeyValuePair<BinTreeProperty, BinTreeProperty>? matchFromSelector = map.Map.FirstOrDefault(k => ((BinTreeHash)k.Key).Value == clipName.Value);
                    if (matchFromSelector == null)
                        break;
                    TryGetAnimationData(map, (BinTreeStructure)matchFromSelector.Value.Value, out animationData);
                    break;
                case 1540989414: //AtomicClipData
                    animationData = (BinTreeEmbedded)structure.Properties.FirstOrDefault(p => p.NameHash == 3030349134); //mAnimationResourceData
                    break;
                // Ideally, these animations are played in the correct sequence, but I'm definitely not good enough at C#
                // to be able to do that. I'm not sure if it's possible in glTF if the animations are different framerates.
                case 2368776128: //SequencerClipData
                    var clips = (BinTreeContainer)structure.Properties.FirstOrDefault(p => p.NameHash == 126660569); //mClipNameList
                    if (clips == null)
                        break;
                    IDictionary<uint, int> hashCount = new Dictionary<uint, int>(); 
                    foreach (BinTreeHash clip in clips.Properties)
                    {
                        if (hashCount.ContainsKey(clip.Value))
                            hashCount[clip.Value] += 1;
                        else
                            hashCount[clip.Value] = 1;
                    }
                    var list = hashCount.ToList();
                    list.Sort((k1, k2) => k1.Value.CompareTo(k2.Value));
                    KeyValuePair<BinTreeProperty, BinTreeProperty>? matchFromSequencer = map.Map.FirstOrDefault(k => ((BinTreeHash)k.Key).Value == list[^1].Key);
                    if (matchFromSequencer == null)
                        break;
                    TryGetAnimationData(map, (BinTreeStructure)matchFromSequencer.Value.Value, out animationData);
                    break;
                case 559985644: //ParallelClipData
                    break;
                case 2394679778: //ConditionFloatClipData
                case 4071811009: //ParametricClipData
                    var parametricClips = (BinTreeContainer)structure.Properties.FirstOrDefault(p => p.NameHash == 784579174 || p.NameHash == 589950661); //mParametricPairDataList || mConditionFloatPairDataList
                    if (parametricClips == null)
                        break;
                    BinTreeHash parametricClipName = null;
                    foreach (BinTreeEmbedded clip in parametricClips.Properties)
                    {
                        if (clip.Properties.Count != 1)
                            continue;
                        parametricClipName = (BinTreeHash)clip.Properties.FirstOrDefault(p => p.NameHash == 3391849597); //mClipName
                    }
                    if (parametricClipName == null)
                        break;
                    KeyValuePair<BinTreeProperty, BinTreeProperty>? matchFromParametric = map.Map.FirstOrDefault(k => ((BinTreeHash)k.Key).Value == parametricClipName.Value);
                    if (matchFromParametric == null)
                        break;
                    TryGetAnimationData(map, (BinTreeStructure)matchFromParametric.Value.Value, out animationData);
                    break;
                case 358669516: //ConditionBoolClipData
                    break;
                default:
                    break;
            }
            if (animationData == null)
                return false;
            return true;
        }

        private void ParseBinTree(BinTree tree)
        {
            foreach (var binTreeObject in tree.Objects)
                ParseBinTreeObject(binTreeObject);
        }

        private void ParseBinTreeContainer(BinTreeContainer container)
        {
            foreach (var property in container.Properties)
                ParseBinTreeEmbedded((BinTreeEmbedded)property);
        }

        private void ParseBinTreeEmbedded(BinTreeEmbedded tree)
        {
            switch (tree.MetaClassHash)
            {
                case 1628559524: //SkinMeshDataProperties
                    foreach (var property in tree.Properties)
                        ParseBinTreeProperty(property);
                    break;
                case 2340045716: //SkinMeshDataProperties_MaterialOverride
                    var materialProperty = tree.Properties.FirstOrDefault(p => p.NameHash == 3538210912); //material
                    var submeshProperty = tree.Properties.FirstOrDefault(p => p.NameHash == 2866241836); //submesh
                    var textureProperty = tree.Properties.FirstOrDefault(p => p.NameHash == 1013213428); //texture
                    Materials.Add(new Material(materialProperty, submeshProperty, textureProperty));
                    break;
            }

        }

        private void ParseBinTreeObject(BinTreeObject treeObject)
        {
            switch (treeObject.MetaClassHash)
            {
                case 2607278582: //SkinCharacterDataProperties
                    foreach (var property in treeObject.Properties)
                        ParseBinTreeProperty(property);
                    break;
                case 4288492553: //StaticMaterialDef
                    if (treeObject.PathHash == MaterialHash)
                    {
                        if (Utils.FindTexture(treeObject, out var texture))
                            Texture = texture;
                    }
                    foreach (var material in Materials.Where(m => m.Hash == treeObject.PathHash && !m.IsComplete))
                        material.Complete(treeObject);
                    break;
            }
        }

        private void ParseBinTreeProperty(BinTreeProperty property)
        {
            switch (property.NameHash)
            {
                case 1174362372: //skinMeshProperties
                    ParseBinTreeEmbedded((BinTreeEmbedded)property);
                    break;
                case 2974586734: //skeleton
                    Skeleton = ((BinTreeString)property).Value.ToLower().Replace('/', '\\');
                    break;
                case 3600813558: //simpleSkin
                    Mesh = ((BinTreeString)property).Value.ToLower().Replace('/', '\\');
                    break;
                case 1013213428: //texture
                    Texture = ((BinTreeString)property).Value.ToLower().Replace('/', '\\');
                    break;
                case 2159540111: //initialSubmeshToHide
                    foreach (var submesh in ((BinTreeString)property).Value.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        RemoveMeshes.Add(submesh.ToLower());
                    break;
                case 611473680: //materialOverride
                    ParseBinTreeContainer((BinTreeContainer)property);
                    break;
                case 3538210912: //material
                    if (((BinTreeEmbedded)property.Parent).MetaClassHash != 1628559524) //SkinMeshDataProperties
                        throw new NotImplementedException();
                    MaterialHash = ((BinTreeObjectLink)property).Value;
                    break;
            }
        }

        public void Save(MainWindowViewModel viewModel, LoggingWindowViewModel loggingViewModel)
        {
            if (!File.Exists(Mesh))
                return;
            SimpleSkin simpleSkin;
            try
            {
                simpleSkin = new SimpleSkin(Mesh);
            }
            catch (Exception)
            {
                loggingViewModel.AddLine($"Couldn't parse {Mesh}", 2);
                return;
            }
            var materialTextures = new Dictionary<string, MagickImage>();
            IDictionary<string, MagickImage> textures = new Dictionary<string, MagickImage>();
            for (var i = 0; i < simpleSkin.Submeshes.Count; i++)
            {
                var submesh = simpleSkin.Submeshes[i];
                if (RemoveMeshes.Contains(submesh.Name.ToLower()))
                {
                    simpleSkin.Submeshes.RemoveAt(i);
                    i--;
                    continue;
                }
                var material = Materials.FirstOrDefault(m => m.Name == submesh.Name.ToLower());
                string textureFile;
                if (material == null)
                    textureFile = Texture;
                else
                    textureFile = material.Texture;
                if (!textures.ContainsKey(textureFile))
                    textures[textureFile] = new MagickImage(textureFile);
                materialTextures[submesh.Name] = textures[textureFile];
            }
            var folderPath = @$"export\{Character}";
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
            SharpGLTF.Schema2.ModelRoot gltf;
            if (!viewModel.IncludeSkeletons)
            {
                gltf = simpleSkin.ToGltf(materialTextures);
                gltf.ApplyBasisTransform(Converter.Config.ScaleMatrix);
            }
            else
            {
                Skeleton skeleton;
                try
                {
                    skeleton = new Skeleton(Skeleton);
                }
                catch (Exception)
                {
                    loggingViewModel.AddLine($"Couldn't parse {Skeleton}", 2);
                    return;
                }
                if (Animations == null)
                    gltf = simpleSkin.ToGltf(skeleton, materialTextures);
                else
                    gltf = simpleSkin.ToGltf(skeleton, materialTextures, Animations);
            }
            gltf.SaveGLB(@$"{folderPath}\{Name}.glb");
        }

        public Skin(string character, string name, BinTree tree, MainWindowViewModel viewModel, LoggingWindowViewModel loggingViewModel)
        {
            Character = character;
            Name = name;
            if (!viewModel.IncludeHiddenMeshes
                && Converter.Config.IgnoreMeshes.ContainsKey(character)
                && Converter.Config.IgnoreMeshes[character].ContainsKey(name))
                RemoveMeshes = new List<string>(Converter.Config.IgnoreMeshes[character][name]);
            else
                RemoveMeshes = new List<string>();
            ParseBinTree(tree);
            if (viewModel.IncludeHiddenMeshes)
                RemoveMeshes = new List<string>();
            if (viewModel.IncludeAnimations)
            {
                Animations = new List<(string, Animation)>();
                foreach (var filePath in tree.Dependencies)
                {
                    if (filePath.ToLower().Contains("/animations/") && File.Exists(filePath))
                        try
                        {
                            AddAnimations(filePath, loggingViewModel);
                        }
                        catch (Exception)
                        {
                            loggingViewModel.AddLine($"Couldn't add animations", 2);
                            return;
                        }
                }
            }
        }
    }
}
