namespace LTESingleLinkAJM.Models
{
    public enum EarSide
    {
        Left,
        Right,
    }

    public static class EarSideExtensions
    {
        public static string Label(this EarSide s) => s == EarSide.Left ? "左耳" : "右耳";

        /// <summary>按 Excel 的命名约定拼波形 PNG 名字: "B1 18050 左耳TX波形.png"</summary>
        public static string WaveformFileName(string band, int earfcn, EarSide side, string txOrRx)
            => $"{band} {earfcn} {side.Label()}{txOrRx}波形.png";
    }
}
