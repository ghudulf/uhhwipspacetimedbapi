// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class Bus
    {
        [DataMember(Name = "BusId")]
        public uint BusId;
        [DataMember(Name = "Model")]
        public string Model;
        [DataMember(Name = "RegistrationNumber")]
        public string? RegistrationNumber;
        [DataMember(Name = "IsActive")]
        public bool IsActive;

        public Bus(
            uint BusId,
            string Model,
            string? RegistrationNumber,
            bool IsActive
        )
        {
            this.BusId = BusId;
            this.Model = Model;
            this.RegistrationNumber = RegistrationNumber;
            this.IsActive = IsActive;
        }

        public Bus()
        {
            this.Model = "";
        }
    }
}
