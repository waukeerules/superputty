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

        /// <summary>
        /// When true the value is a secret (e.g. a password): it's stored encrypted in <see cref="ProtectedValue"/>
        /// instead of <see cref="Value"/>, and masked in dialogs and previews
        /// </summary>
        public bool Secret { get; set; }

        /// <summary>
        /// The value of a secret, encrypted with Windows DPAPI for the current user, as base64. Set and read by
        /// Utils.EnvironmentVariablesManager when saving and loading; <see cref="Value"/> holds the plain text in memory.
        /// </summary>
        public string ProtectedValue { get; set; }

        /// <summary>Json.NET: write the plain value only for variables that aren't secret</summary>
        /// <returns>true to write <see cref="Value"/></returns>
        public bool ShouldSerializeValue()
        {
            return !this.Secret;
        }

        /// <summary>Json.NET: write the encrypted value only for secrets</summary>
        /// <returns>true to write <see cref="ProtectedValue"/></returns>
        public bool ShouldSerializeProtectedValue()
        {
            return this.Secret;
        }
    }

    public class EnvironmentVariables
    {
        public BindingList<EnvironmentVariable> Variables { get; set; } = new BindingList<EnvironmentVariable>();
    }
}
