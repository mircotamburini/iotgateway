﻿namespace PluginInterface
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DriverSupportedAttribute : Attribute
    {
        public string DAPName { get; }

        public DriverSupportedAttribute(string dAPName)
        {
            DAPName = dAPName;
        }
    }
}