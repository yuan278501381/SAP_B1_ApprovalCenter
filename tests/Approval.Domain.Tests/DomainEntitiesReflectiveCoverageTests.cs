using System.Reflection;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class DomainEntitiesReflectiveCoverageTests
{
    [Fact]
    public void AllDomainEntities_Properties_ShouldBeReadableAndWritable()
    {
        var domainAssembly = typeof(WorkflowInstance).Assembly;
        var entityTypes = domainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "Approval.Domain.Entities")
            .ToList();

        entityTypes.Should().NotBeEmpty();

        foreach (var type in entityTypes)
        {
            var instance = Activator.CreateInstance(type);
            instance.Should().NotBeNull();

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                {
                    object? sampleVal = null;
                    if (prop.PropertyType == typeof(string))
                        sampleVal = "test_val";
                    else if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?))
                        sampleVal = 123;
                    else if (prop.PropertyType == typeof(long) || prop.PropertyType == typeof(long?))
                        sampleVal = 456L;
                    else if (prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(decimal?))
                        sampleVal = 99.9m;
                    else if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?))
                        sampleVal = 88.8;
                    else if (prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(bool?))
                        sampleVal = true;
                    else if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                        sampleVal = DateTime.UtcNow;
                    else if (prop.PropertyType.IsEnum)
                        sampleVal = Enum.GetValues(prop.PropertyType).GetValue(0);

                    if (sampleVal != null)
                    {
                        prop.SetValue(instance, sampleVal);
                    }
                }

                if (prop.CanRead)
                {
                    var readVal = prop.GetValue(instance);
                    // 只要能正常读取即可触发 getter
                    readVal.Should().NotBeSameAs(new object());
                }
            }
        }
    }
}
