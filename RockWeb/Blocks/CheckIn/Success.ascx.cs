﻿// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Web.UI;
using System.Web.UI.HtmlControls;

using Rock;
using Rock.Attribute;
using Rock.CheckIn;
using Rock.Web.UI;

namespace RockWeb.Blocks.CheckIn
{
    /// <summary>
    /// 
    /// </summary>
    [DisplayName( "Success" )]
    [Category( "Check-in" )]
    [Description( "Displays the details of a successful checkin." )]

    [LinkedPage( "Person Select Page", "", false, "", "", 5 )]
    [TextField( "Title", "", false, "Checked-in", "Text", 6 )]
    [TextField( "Detail Message", "The message to display indicating person has been checked in. Use {0} for person, {1} for group, {2} for schedule, and {3} for the security code", false,
        "{0} was checked into {1} in {2} at {3}", "Text", 7 )]

    public partial class Success : CheckInBlock
    {
        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );

            RockPage.AddScriptLink( "~/Scripts/CheckinClient/cordova-2.4.0.js", false );
            RockPage.AddScriptLink( "~/Scripts/CheckinClient/ZebraPrint.js" );
            RockPage.AddScriptLink( "~/Scripts/CheckinClient/checkin-core.js" );

            var bodyTag = this.Page.Master.FindControl( "bodyTag" ) as HtmlGenericControl;
            if ( bodyTag != null )
            {
                bodyTag.AddCssClass( "checkin-success-bg" );
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Load" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnLoad( EventArgs e )
        {
            base.OnLoad( e );

            if ( CurrentWorkflow == null || CurrentCheckInState == null )
            {
                NavigateToHomePage();
            }
            else
            {
                if ( !Page.IsPostBack )
                {
                    try
                    {
                        lTitle.Text = GetAttributeValue( "Title" );
                        string detailMsg = GetAttributeValue( "DetailMessage" );

                        var printFromClient = new List<CheckInLabel>();
                        var printFromServer = new List<CheckInLabel>();

                        // Print the labels
                        foreach ( var family in CurrentCheckInState.CheckIn.Families.Where( f => f.Selected ) )
                        {
                            lbAnother.Visible =
                                CurrentCheckInState.CheckInType.TypeOfCheckin == TypeOfCheckin.Individual &&
                                family.People.Count > 1;

                            foreach ( var person in family.GetPeople( true ) )
                            {
                                foreach ( var groupType in person.GetGroupTypes( true ) )
                                {
                                    foreach ( var group in groupType.GetGroups( true ) )
                                    {
                                        foreach ( var location in group.GetLocations( true ) )
                                        {
                                            foreach ( var schedule in location.GetSchedules( true ) )
                                            {
                                                var li = new HtmlGenericControl( "li" );
                                                li.InnerText = string.Format( detailMsg, person.ToString(), group.ToString(), location.ToString(), schedule.ToString(), person.SecurityCode );

                                                phResults.Controls.Add( li );
                                            }
                                        }
                                    }

                                    if ( groupType.Labels != null && groupType.Labels.Any() )
                                    {
                                        printFromClient.AddRange( groupType.Labels.Where( l => l.PrintFrom == Rock.Model.PrintFrom.Client ) );
                                        printFromServer.AddRange( groupType.Labels.Where( l => l.PrintFrom == Rock.Model.PrintFrom.Server ) );
                                    }
                                }
                            }
                        }

                        if ( printFromClient.Any() )
                        {
                            var urlRoot = string.Format( "{0}://{1}", Request.Url.Scheme, Request.Url.Authority );
                            printFromClient
                                .OrderBy( l => l.PersonId )
                                .ThenBy( l => l.Order )
                                .ToList()
                                .ForEach( l => l.LabelFile = urlRoot + l.LabelFile );
                            AddLabelScript( printFromClient.ToJson() );
                        }

                        if ( printFromServer.Any() )
                        {
                            Socket socket = null;
                            string currentIp = string.Empty;

                            foreach ( var label in printFromServer
                                .OrderBy( l => l.PersonId )
                                .ThenBy( l => l.Order ) )
                            {
                                var labelCache = KioskLabel.Read( label.FileGuid );
                                if ( labelCache != null )
                                {
                                    if ( !string.IsNullOrWhiteSpace( label.PrinterAddress ) )
                                    {
                                        if ( label.PrinterAddress != currentIp )
                                        {
                                            if ( socket != null && socket.Connected )
                                            {
                                                socket.Shutdown( SocketShutdown.Both );
                                                socket.Close();
                                            }

                                            currentIp = label.PrinterAddress;
                                            int printerPort = 9100;
                                            var printerIp = currentIp;

                                            // If the user specified in 0.0.0.0:1234 syntax then pull our the IP and port numbers.
                                            if ( printerIp.Contains( ":" ) )
                                            {
                                                var segments = printerIp.Split( ':' );

                                                printerIp = segments[0];
                                                printerPort = segments[1].AsInteger();
                                            }

                                            var printerEndpoint = new IPEndPoint( IPAddress.Parse( currentIp ), printerPort );

                                            socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );
                                            IAsyncResult result = socket.BeginConnect( printerEndpoint, null, null );
                                            bool success = result.AsyncWaitHandle.WaitOne( 5000, true );
                                        }

                                        string printContent = labelCache.FileContent;
                                        foreach ( var mergeField in label.MergeFields )
                                        {
                                            if ( !string.IsNullOrWhiteSpace( mergeField.Value ) )
                                            {
                                                printContent = Regex.Replace( printContent, string.Format( @"(?<=\^FD){0}(?=\^FS)", mergeField.Key ), ZebraFormatString( mergeField.Value ) );
                                            }
                                            else
                                            {
                                                // Remove the box preceding merge field
                                                printContent = Regex.Replace( printContent, string.Format( @"\^FO.*\^FS\s*(?=\^FT.*\^FD{0}\^FS)", mergeField.Key ), string.Empty );
                                                // Remove the merge field
                                                printContent = Regex.Replace( printContent, string.Format( @"\^FD{0}\^FS", mergeField.Key ), "^FD^FS" );
                                            }
                                        }

                                        if ( socket.Connected )
                                        {
                                            var ns = new NetworkStream( socket );
                                            byte[] toSend = System.Text.Encoding.ASCII.GetBytes( printContent );
                                            ns.Write( toSend, 0, toSend.Length );
                                        }
                                        else
                                        {
                                            phResults.Controls.Add( new LiteralControl( "<br/>NOTE: Could not connect to printer!" ) );
                                        }
                                    }
                                }
                            }

                            if ( socket != null && socket.Connected )
                            {
                                socket.Shutdown( SocketShutdown.Both );
                                socket.Close();
                            }
                        }

                    }
                    catch ( Exception ex )
                    {
                        LogException( ex );
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Click event of the lbDone control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void lbDone_Click( object sender, EventArgs e )
        {
            NavigateToHomePage();
        }

        private string ZebraFormatString( string input, bool isJson = false )
        {
            if ( isJson )
            {
                return input.Replace( "é", @"\\82" );  // fix acute e
            }
            else
            {
                return input.Replace( "é", @"\82" );  // fix acute e
            }
        }

        /// <summary>
        /// Handles the Click event of the lbAnother control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void lbAnother_Click( object sender, EventArgs e )
        {
            if ( KioskCurrentlyActive )
            {
                foreach ( var family in CurrentCheckInState.CheckIn.Families.Where( f => f.Selected ) )
                {
                    foreach ( var person in family.People.Where( p => p.Selected ) )
                    {
                        person.Selected = false;

                        foreach ( var groupType in person.GroupTypes.Where( g => g.Selected ) )
                        {
                            groupType.Selected = false;
                        }
                    }
                }

                SaveState();
                NavigateToLinkedPage( "PersonSelectPage" );

            }
            else
            {
                NavigateToHomePage();
            }
        }

        /// <summary>
        /// Adds the label script.
        /// </summary>
        /// <param name="jsonObject">The json object.</param>
        private void AddLabelScript( string jsonObject )
        {
            string script = string.Format( @"

        // setup deviceready event to wait for cordova
	    if (navigator.userAgent.match(/(iPhone|iPod|iPad)/)) {{
            document.addEventListener('deviceready', onDeviceReady, false);
        }} else {{
            $( document ).ready(function() {{
                onDeviceReady();
            }});
        }}

	    // label data
        var labelData = {0};

		function onDeviceReady() {{
            try {{			
                printLabels();
            }} 
            catch (err) {{
                console.log('An error occurred printing labels: ' + err);
            }}
		}}
		
		function alertDismissed() {{
		    // do something
		}}
		
		function printLabels() {{
		    ZebraPrintPlugin.printTags(
            	JSON.stringify(labelData), 
            	function(result) {{ 
			        console.log('Tag printed');
			    }},
			    function(error) {{   
				    // error is an array where:
				    // error[0] is the error message
				    // error[1] determines if a re-print is possible (in the case where the JSON is good, but the printer was not connected)
			        console.log('An error occurred: ' + error[0]);
                    navigator.notification.alert(
                        'An error occurred while printing the labels.' + error[0],  // message
                        alertDismissed,         // callback
                        'Error',            // title
                        'Ok'                  // buttonName
                    );
			    }}
            );
	    }}
", ZebraFormatString( jsonObject, true ) );
            ScriptManager.RegisterStartupScript( this, this.GetType(), "addLabelScript", script, true );
        }

    }
}