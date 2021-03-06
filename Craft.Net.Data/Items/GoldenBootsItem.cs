namespace Craft.Net.Data.Items
{

    public class GoldenBootsItem : ToolItem, IArmorItem
    {
        public override short Id
        {
            get
            {
                return 317;
            }
        }

        public int ArmorBonus
        {
            get { return 1; }
        }

        public ArmorSlot ArmorSlot
        {
            get { return ArmorSlot.Footwear; }
        }

        public override ToolType ToolType
        {
            get { return ToolType.Other; }
        }

        public override ToolMaterial ToolMaterial
        {
            get { return ToolMaterial.Gold; }
        }
    }
}
