﻿using BioEngine.Core.DB;

namespace BioEngine.Core.Entities.Blocks
{
    [TypedEntity("picture")]
    public class PictureBlock : ContentBlock<PictureBlockData>
    {
        public override string TypeTitle { get; set; } = "Галерея";

        public override string ToString()
        {
            return $"Картинка: {Data.Picture.FileName}";
        }
    }

    public class PictureBlockData : ContentBlockData
    {
        public StorageItem Picture { get; set; }
        public string Url { get; set; }
    }
}