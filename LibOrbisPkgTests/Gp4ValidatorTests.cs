using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LibOrbisPkg.GP4;

namespace LibOrbisPkgTests
{
  [TestClass]
  public class Gp4ValidatorTests
  {
    static ValidateResult RunCheck(string name, params string[] paths)
    {
      var proj = new Gp4Project
      {
        files = new Files { Items = paths.Select(p => new Gp4File { TargetPath = p, OrigPath = p }).ToList() },
      };
      var check = typeof(Gp4Validator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
      return (ValidateResult)check.Invoke(null, new object[] { proj, "" });
    }

    [TestMethod]
    public void CaseOnlyDuplicates_AreReported()
    {
      var r = RunCheck("checkCaseOnlyDuplicates", "Asset/a.xxx", "Asset/A.XXX", "Asset/b.xxx");
      Assert.IsNotNull(r);
      Assert.AreEqual(ValidateResult.ResultType.Warning, r.Type);
      StringAssert.Contains(r.Message, "Asset/a.xxx");
    }

    [TestMethod]
    public void DistinctPaths_AreNotReported()
    {
      Assert.IsNull(RunCheck("checkCaseOnlyDuplicates", "Asset/a.xxx", "Asset/b.xxx", "sce_sys/param.sfo"));
    }
  }
}
