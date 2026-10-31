namespace BaseConquest.Battle
{
    // 只在明确查看时续时；内容刷新和人物移动不延长提示。
    public sealed class TransientNotice
    {
        public const double Duration=3;
        private double until=double.NegativeInfinity;
        public void Show(double now){until=now+Duration;}
        public void Dismiss(){until=double.NegativeInfinity;}
        public bool Visible(double now)=>now<until;
    }
}
