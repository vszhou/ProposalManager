﻿// Copyright(c) Microsoft Corporation. 
// All rights reserved.
//
// Licensed under the MIT license. See LICENSE file in the solution root folder for full license information

using ApplicationCore.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace ApplicationCore.Entities
{
    public class TeamMember : BaseEntity<TeamMember>
    {
        /// <summary>
        /// User display name
        /// </summary>
        [JsonProperty("displayName", Order = 1)]
        public string DisplayName { get; set; }

        /// <summary>
        /// The values for the user profile fields
        /// </summary>
        [JsonProperty("fields", Order = 2)]
        public TeamMemberFields Fields { get; set; }

        [JsonProperty("assignedRole", Order = 3)]
        public Role AssignedRole { get; set; }

        [JsonProperty("processStep", Order = 4)]
        public string ProcessStep { get; set; }

        /// <summary>
        /// Represents the empty user profile. This field is read-only.
        /// </summary>
        public static TeamMember Empty
        {
            get => new TeamMember
            {
                Id = String.Empty,
                DisplayName = String.Empty,
                AssignedRole = Role.Empty,
                Fields = TeamMemberFields.Empty,
                ProcessStep = String.Empty
            };
        }  
    }

    public class TeamMemberFields
    {
        /// <summary>
        /// User email
        /// </summary>
        [JsonProperty("mail")]
        public string Mail { get; set; }

        /// <summary>
        /// User Principal Name
        /// </summary>
        [JsonProperty("userPrincipalName")]
        public string UserPrincipalName { get; set; }

        /// <summary>
        /// User title
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// Represents the empty user profile. This field is read-only.
        /// </summary>
        public static TeamMemberFields Empty
        {
            get => new TeamMemberFields
            {
                Mail = String.Empty,
                UserPrincipalName = String.Empty,
                Title = String.Empty
            };
        }
    }
}
