using Dorc.Api.Validation;
using Dorc.ApiModel;

namespace Dorc.Api.Tests.Validation
{
    [TestClass]
    public class TerraformExclusivityValidatorTests
    {
        [TestMethod]
        public void Validate_OnlyDirectSource_Passes()
        {
            var c = new ComponentApiModel { ComponentName = "x", ScriptPath = "/s/path.tf" };
            TerraformExclusivityValidator.Validate(c);
        }

        [TestMethod]
        public void Validate_OnlyCatalogRef_Passes()
        {
            var c = new ComponentApiModel
            {
                ComponentName = "x",
                TerraformTemplateName = "vnet",
                TerraformTemplateVersion = "1.0.0"
            };
            TerraformExclusivityValidator.Validate(c);
        }

        [TestMethod]
        public void Validate_NeitherSet_Passes()
        {
            var c = new ComponentApiModel { ComponentName = "x" };
            TerraformExclusivityValidator.Validate(c);
        }

        [TestMethod]
        public void Validate_BothSet_Throws()
        {
            var c = new ComponentApiModel
            {
                ComponentName = "x",
                ScriptPath = "/s/path.tf",
                TerraformTemplateName = "vnet",
                TerraformTemplateVersion = "1.0.0"
            };

            var ex = Assert.ThrowsExactly<TerraformExclusivityValidator.ValidationException>(
                () => TerraformExclusivityValidator.Validate(c));
            StringAssert.Contains(ex.Message, "mutually exclusive");
            StringAssert.Contains(ex.Message, "x");
        }

        [TestMethod]
        public void Validate_TemplateNameWithoutVersion_Throws()
        {
            var c = new ComponentApiModel
            {
                ComponentName = "x",
                TerraformTemplateName = "vnet"
            };

            Assert.ThrowsExactly<TerraformExclusivityValidator.ValidationException>(
                () => TerraformExclusivityValidator.Validate(c));
        }

        [TestMethod]
        public void Validate_TemplateVersionWithoutName_Throws()
        {
            var c = new ComponentApiModel
            {
                ComponentName = "x",
                TerraformTemplateVersion = "1.0.0"
            };

            Assert.ThrowsExactly<TerraformExclusivityValidator.ValidationException>(
                () => TerraformExclusivityValidator.Validate(c));
        }

        [TestMethod]
        public void ValidateAll_RecursesIntoChildren()
        {
            var bad = new ComponentApiModel
            {
                ComponentName = "child-bad",
                ScriptPath = "/s/path.tf",
                TerraformTemplateName = "vnet",
                TerraformTemplateVersion = "1.0.0"
            };
            var parent = new ComponentApiModel
            {
                ComponentName = "parent",
                Children = new List<ComponentApiModel> { bad }
            };

            Assert.ThrowsExactly<TerraformExclusivityValidator.ValidationException>(
                () => TerraformExclusivityValidator.ValidateAll(new[] { parent }));
        }

        [TestMethod]
        public void ValidateAll_NullComponents_DoesNotThrow()
        {
            TerraformExclusivityValidator.ValidateAll(null!);
        }
    }
}
