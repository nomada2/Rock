
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by the Rock.CodeGeneration project
//     Changes to this file will be lost when the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
//
// THIS WORK IS LICENSED UNDER A CREATIVE COMMONS ATTRIBUTION-NONCOMMERCIAL-
// SHAREALIKE 3.0 UNPORTED LICENSE:
// http://creativecommons.org/licenses/by-nc-sa/3.0/
//
using System.ComponentModel;
using System.ComponentModel.Composition;

namespace Rock.DataFilters.Fund
{
    /// <summary>
    /// Other Fund Data View Filter
    /// </summary>
    [Description( "Filter Funds using other data view" )]
    [Export( typeof( DataFilterComponent ) )]
    [ExportMetadata( "ComponentName", "Other Fund Data View Filter" )]
    public partial class FundDataViewFilter : OtherDataViewFilter<Rock.Model.FinancialAccount>
    {
    }
}
