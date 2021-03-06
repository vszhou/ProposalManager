﻿// Copyright(c) Microsoft Corporation. 
// All rights reserved.
//
// Licensed under the MIT license. See LICENSE file in the solution root folder for full license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace SmartLink.Service
{
    public class CheckDocumentEntity : TableEntity
    {
        public CheckDocumentEntity()
        {
            this.PartitionKey = Guid.NewGuid().ToString();
            this.RowKey = Guid.NewGuid().ToString();
        }

        public string Comments { get; set; }
    }
}
