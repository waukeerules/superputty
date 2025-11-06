using System.Collections.Generic;
using System.ComponentModel;

namespace SuperPutty.Models
{
    public class EnvironmentVariable
    {
        public EnvironmentVariable()
        {
            Key = "";
            Value = "";
        }

        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class EnvironmentVariables
    {
        public BindingList<EnvironmentVariable> Variables { get; set; } = new BindingList<EnvironmentVariable>();
    }
}