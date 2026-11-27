// <copyright file="PrivilegeLevelTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

/// <summary>
/// Unit tests for the <see cref="PrivilegeLevel"/> enum.
/// </summary>
[TestFixture]
public class PrivilegeLevelTests
{
    /// <summary>
    /// Verifies that User has value 0 (least privilege).
    /// </summary>
    [Test]
    public void PrivilegeLevel_User_HasValueZero()
    {
        Assert.That((byte)PrivilegeLevel.User, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that Hypervisor has value 2.
    /// </summary>
    [Test]
    public void PrivilegeLevel_Hypervisor_HasValueTwo()
    {
        Assert.That((byte)PrivilegeLevel.Hypervisor, Is.EqualTo(2));
    }

    /// <summary>
    /// Verifies that privilege levels are ordered as User &lt; Kernel &lt; Hypervisor.
    /// </summary>
    [Test]
    public void PrivilegeLevel_LevelsAreOrderedCorrectly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrivilegeLevel.User, Is.LessThan(PrivilegeLevel.Kernel));
            Assert.That(PrivilegeLevel.Kernel, Is.LessThan(PrivilegeLevel.Hypervisor));
        });
    }

    /// <summary>
    /// Verifies that all expected privilege levels exist.
    /// </summary>
    [Test]
    public void PrivilegeLevel_ContainsAllExpectedValues()
    {
        var values = Enum.GetValues<PrivilegeLevel>();

        Assert.That(values, Has.Length.EqualTo(4));
        Assert.That(values, Contains.Item(PrivilegeLevel.User));
        Assert.That(values, Contains.Item(PrivilegeLevel.Kernel));
        Assert.That(values, Contains.Item(PrivilegeLevel.Hypervisor));
        Assert.That(values, Contains.Item(PrivilegeLevel.Reserved));
    }
}