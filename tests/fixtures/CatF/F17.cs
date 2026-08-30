using System;

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestMethodAttribute : Attribute { }
}

namespace CatF.F17
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    public sealed class MstestTests
    {
        [TestMethod]
        public void RunsByMSTest() => Helper();

        private static void Helper() { }

        public void DeadSibling() { }
    }
}
