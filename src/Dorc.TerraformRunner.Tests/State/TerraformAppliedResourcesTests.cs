using Dorc.TerraformRunner.State;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformAppliedResourcesTests
    {
        [TestMethod]
        public void ParseShowJson_NullEmptyOrInvalid_ReturnsEmpty()
        {
            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson(null).Count);
            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson("").Count);
            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson("not json").Count);
            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson("{}").Count);
            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson("{\"values\":{}}").Count);
        }

        [TestMethod]
        public void ParseShowJson_ManagedAzureResource_MapsAllFields()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    {
                      "address": "azurerm_resource_group.main",
                      "mode": "managed",
                      "type": "azurerm_resource_group",
                      "name": "main",
                      "provider_name": "registry.terraform.io/hashicorp/azurerm",
                      "values": {
                        "name": "rg-sh-dv-demo",
                        "id": "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-sh-dv-demo"
                      }
                    }
                  ]
                }
              }
            }
            """;

            var resources = TerraformAppliedResources.ParseShowJson(json);

            Assert.AreEqual(1, resources.Count);
            var resource = resources[0];
            Assert.AreEqual("rg-sh-dv-demo", resource.Name);
            Assert.AreEqual("Azure", resource.Provider);
            Assert.AreEqual("azurerm_resource_group", resource.ResourceType);
            Assert.AreEqual("/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-sh-dv-demo", resource.ResourceIdentifier);
            Assert.AreEqual("7c7c1f8f-f295-456c-81e4-5d508579d93e", resource.Subscription);
        }

        [TestMethod]
        public void ParseShowJson_DataSources_AreExcluded()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    {
                      "mode": "data",
                      "type": "azurerm_resource_group",
                      "name": "existing",
                      "provider_name": "registry.terraform.io/hashicorp/azurerm",
                      "values": { "name": "rg-existing", "id": "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-existing" }
                    }
                  ]
                }
              }
            }
            """;

            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson(json).Count);
        }

        [TestMethod]
        public void ParseShowJson_ChildModules_AreTraversedRecursively()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "child_modules": [
                    {
                      "resources": [
                        {
                          "mode": "managed",
                          "type": "aiven_kafka_topic",
                          "name": "topic",
                          "provider_name": "registry.terraform.io/aiven/aiven",
                          "values": { "name": "orders", "id": "trading-traveler/traveler-non-prod/orders" }
                        }
                      ],
                      "child_modules": [
                        {
                          "resources": [
                            {
                              "mode": "managed",
                              "type": "azurerm_servicebus_queue",
                              "name": "queue",
                              "provider_name": "registry.terraform.io/hashicorp/azurerm",
                              "values": { "name": "sbq-demo", "id": "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/ns/queues/sbq-demo" }
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

            var resources = TerraformAppliedResources.ParseShowJson(json);

            Assert.AreEqual(2, resources.Count);
            Assert.AreEqual("Aiven", resources.Single(r => r.Name == "orders").Provider);
            Assert.IsNull(resources.Single(r => r.Name == "orders").Subscription);
            Assert.AreEqual("Azure", resources.Single(r => r.Name == "sbq-demo").Provider);
        }

        [TestMethod]
        public void ParseShowJson_DuplicateIdentifiersAcrossModuleInstances_AreDeduped()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    { "mode": "managed", "type": "azurerm_resource_group", "name": "a", "provider_name": "registry.terraform.io/hashicorp/azurerm", "values": { "name": "rg-one", "id": "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/RG-ONE" } },
                    { "mode": "managed", "type": "azurerm_resource_group", "name": "b", "provider_name": "registry.terraform.io/hashicorp/azurerm", "values": { "name": "rg-one", "id": "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/resourceGroups/rg-one" } }
                  ]
                }
              }
            }
            """;

            Assert.AreEqual(1, TerraformAppliedResources.ParseShowJson(json).Count);
        }

        [TestMethod]
        public void ParseShowJson_UnknownProvider_FallsBackToLastSourceSegment()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    { "mode": "managed", "type": "random_pet", "name": "pet", "provider_name": "registry.terraform.io/hashicorp/random", "values": { "id": "agile-koala" } }
                  ]
                }
              }
            }
            """;

            var resources = TerraformAppliedResources.ParseShowJson(json);

            Assert.AreEqual(1, resources.Count);
            Assert.AreEqual("random", resources[0].Provider);
            // No cloud-side name attribute: the id is more meaningful than the config block name.
            Assert.AreEqual("agile-koala", resources[0].Name);
        }

        [TestMethod]
        public void ParseShowJson_KafkaTopic_UsesTopicNameNotBlockLabel()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    {
                      "mode": "managed",
                      "type": "aiven_kafka_topic",
                      "name": "this",
                      "provider_name": "registry.terraform.io/aiven/aiven",
                      "values": {
                        "topic_name": "tr.dv.tst.dorc.stockmodule-test.il0",
                        "id": "trading-traveler/traveler-unstable-dev/tr.dv.tst.dorc.stockmodule-test.il0"
                      }
                    }
                  ]
                }
              }
            }
            """;

            var resources = TerraformAppliedResources.ParseShowJson(json);

            Assert.AreEqual(1, resources.Count);
            Assert.AreEqual("tr.dv.tst.dorc.stockmodule-test.il0", resources[0].Name);
            Assert.AreEqual("Aiven", resources[0].Provider);
        }

        [TestMethod]
        public void ParseShowJson_LongIdentifier_IsTruncatedToColumnWidth()
        {
            var longId = "/subscriptions/7c7c1f8f-f295-456c-81e4-5d508579d93e/" + new string('x', 600);
            var json = $$"""
            {
              "values": {
                "root_module": {
                  "resources": [
                    { "mode": "managed", "type": "azurerm_thing", "name": "t", "provider_name": "registry.terraform.io/hashicorp/azurerm", "values": { "name": "thing", "id": "{{longId}}" } }
                  ]
                }
              }
            }
            """;

            var resources = TerraformAppliedResources.ParseShowJson(json);

            Assert.AreEqual(1, resources.Count);
            Assert.AreEqual(500, resources[0].ResourceIdentifier.Length);
            Assert.AreEqual("7c7c1f8f-f295-456c-81e4-5d508579d93e", resources[0].Subscription);
        }

        [TestMethod]
        public void ParseShowJson_ResourceWithoutNameOrId_IsSkipped()
        {
            var json = """
            {
              "values": {
                "root_module": {
                  "resources": [
                    { "mode": "managed", "type": "azurerm_thing", "provider_name": "registry.terraform.io/hashicorp/azurerm", "values": {} }
                  ]
                }
              }
            }
            """;

            Assert.AreEqual(0, TerraformAppliedResources.ParseShowJson(json).Count);
        }
    }
}
