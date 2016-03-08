﻿using System.Text;
using System.Text.RegularExpressions;
using NTemplates.DocumentStructure;
using NTemplates.DocumentFormat;
using NTemplates.EventArgs;
using System.Threading.Tasks;

namespace NTemplates
{
    public enum KeyWords
    {
        SCAN,
        SCAN_FOR,
        ENDSCAN,
        IF,
        ELSE,
        ENDIF,
        NONE
    }

    public enum eTextFormat { RTF, Text }

    public class Parser
    {
        #region instance variables

        private IControlBlock documentNode;
        private StringBuilder _RTFText;
        private MatchCollection matchCollection;
        private OutputNode rootOutputNode;
        private DataManager dataManager;
        internal string KeywordsRegExString;
        internal string FunctionsRegexString;
        IDocumentFormat idFmt;

        #endregion
                
        #region Constants

        internal static string _d = "#";

        internal static string _identif = @"[a-z]([a-z]*[0-9]*)*";
        //Construction Blocks
        internal static string _scan = _d + @"SCAN\(" + _identif + @"\)" + _d;

        //Not used in the current version
        internal static string _count = @"COUNT\(" + _identif + @"\)";
        //Not used in the current version
        internal static string _sum = @"SUM\(" + _identif + @"\)";

        internal static string _endscan = _d + @"ENDSCAN" + _d; 
        
        internal static string _scanfor = _d + @"SCAN\(" + _identif + @"\)\s*FOR\s*\(.+?\)" + _d;
        internal static string _if = _d + @"IF\s*\(.+?\)"+_d;
        internal static string _else = _d + @"ELSE" + _d;
        internal static string _endif = _d + @"ENDIF" + _d;

        //Page Break command
        internal static string _pgbrk = _d + "PAGE_BRK" + _d;

        //Functions
        internal static string _dtfmt = _d + @"Dtfmt\s*\(.+?\)" + _d; //Function "Date Format";    

        internal static string _dbfmt = _d + @"Dbfmt\s*\(.+?\)" + _d; //Function "Double Format";    //internal static string _dbfmt = _d + @"Dbfmt\s*\(([0-9]|.)+?\)" + _d; //Function "Double Format";

        internal static string _hlnk = _d + @"Hlnk\s*\(.+?\)" + _d; //Function "Hyperlink"

        #endregion

        internal Parser(eTextFormat format)
        {
            TextFormat = format;
            DataManager = new DataManager();
           
            KeywordsRegExString =  @"\b*(" + _scan + "|"
                            + _endscan + "|"
                            + _scanfor + "|"
                            + _if + "|"
                            + _else + "|"
                            + _endif +
                            ")\\b*";

            FunctionsRegexString = @"\b*(" + _dtfmt + "|" + _dbfmt + "|" + _hlnk + ")\\b*";
        }

        public eTextFormat TextFormat
        {
            get;
            set;
        }

        internal DataManager DataManager
        {
            get { return dataManager; }
            set { dataManager = value; }
        }

        internal OutputNode CurrentOutputNode
        {
            get;
            set;
        }

        public StringBuilder RTFInput
        {
            get { return _RTFText; }
            set { _RTFText = value; }
        }

        public string RTFOutput { get; set; }

        internal DocumentCreator Creator { get; set; }

        internal void Parse(string text)
        {
            DataManager.ResetRecordPositions();

            RTFInput = CleanPlaceHolders(text);
          

            //Set the starting point for the recursive process    
            rootOutputNode = new OutputNode();
            CurrentOutputNode = rootOutputNode;

            GetControlBlocks();
            documentNode.Expand();

            //Generate the ouput!!
            StringBuilder rootText = rootOutputNode.Text;
            if (rootText != null)
                RTFOutput = rootOutputNode.Text.ToString().Trim();
            else
                RTFOutput = "";

            foreach (OutputNode child in rootOutputNode.Children)
            {
                RTFOutput += child.GetFullText().ToString().Trim();
            }

        }


        internal async Task<string> ParseAsync(string text)
        {
            DataManager.ResetRecordPositions();

            RTFInput = CleanPlaceHolders(text);
            
            //Set the starting point for the recursive process    
            rootOutputNode = new OutputNode();
            CurrentOutputNode = rootOutputNode;

            GetControlBlocks();
            documentNode.Expand();

            //Generate the ouput!!
            StringBuilder rootText = rootOutputNode.Text;
            if (rootText != null)
                RTFOutput = rootOutputNode.Text.ToString().Trim();
            else
                RTFOutput = "";

            foreach (OutputNode child in rootOutputNode.Children)
            {
                RTFOutput += child.GetFullText().ToString().Trim();
            }


      

            return RTFOutput;
        }





        private StringBuilder CleanPlaceHolders(string text)
        {                        
            return new StringBuilder(TextCleaner.CleanText(text, _d+".+?"+_d));
        }

        private void GetControlBlocks()
        {
            PreprocessText();

            Regex regex = CommonMethods.GetRegex(KeywordsRegExString);
            matchCollection = regex.Matches(RTFInput.ToString());

            #region Set up the document's root

            documentNode = new TextBlock(true);
            documentNode.Start = 0;
            if (matchCollection.Count > 0)
            {
                documentNode.End = matchCollection[0].Index - 1;// TODO: CAMBIO matchCollection[0].Index;
            }
            else
            {
                documentNode.End = RTFInput.Length - 1;
            }
            documentNode.InnerText = RTFInput.ToString().InnerString(documentNode.Start, documentNode.End).Trim(); // TODO: CAMBIO RTFInput.ToString().InnerString(documentNode.Start, documentNode.End);
            documentNode.DocumentParser = this;
            IControlBlock currentNode = documentNode;

            #endregion

            int currRegex = 0;
            foreach (Match match in matchCollection)
            {
                #region Create the tree of control blocks
                switch (GetKeyWord(match))
                {
                    case KeyWords.SCAN:
                        {
                            //Closes the Texblok immediately before this SCAN
                            if (!currentNode.IsRoot)
                            {
                                currentNode = CloseControlBlock(currentNode, match);
                            }

                            IControlBlock scan = CreateScan(currentNode, match, false);

                            //Creates a new TextBlock as a child of this scan
                            currentNode = CreateTextBlock(match, scan);

                            break;
                        }
                    case KeyWords.SCAN_FOR:
                        {
                            //Closes the Texblok immediately before this SCAN
                            if (!currentNode.IsRoot)
                            {
                                currentNode = CloseControlBlock(currentNode, match);
                            }

                            IControlBlock scan = CreateScan(currentNode, match, true);

                            currentNode = CreateTextBlock(match, scan);

                            break;
                        }
                    case KeyWords.IF:
                        {
                            //Closes the Texblok immediately before this if
                            if (!currentNode.IsRoot)
                            {
                                currentNode = CloseControlBlock(currentNode, match);
                            }

                            IControlBlock ifBlock = new IfBlock(match,this);
                            ifBlock.OpenRegEx = match;
                            ifBlock.MatchStart = match.Index;
                            ifBlock.Start = match.Index + match.Length;
                            ifBlock.DocumentParser = this;
                            ifBlock.Parent = currentNode;
                            currentNode.Children.Add(ifBlock);

                            //Create a new TextBlock for the "true" part of the if
                            currentNode = CreateTextBlock(match, ifBlock);

                            break;
                        }
                    case KeyWords.ELSE:
                        {
                            //Close the 'true' part of the if
                            currentNode = CloseControlBlock(currentNode, match);

                            // Create the 'Else' part
                            currentNode = CreateTextBlock(match, currentNode);
                            break;
                        }
                    case KeyWords.ENDIF:
                        {
                            
                            //Close the 'true' or the 'false' part of the if                             
                            currentNode = CloseControlBlock(currentNode, match);

                            //Close the embracing 'if' block
                            currentNode = CloseControlBlock(currentNode, match);

                            currentNode = CreateTextBlock(match, currentNode);
                            if (currRegex == matchCollection.Count - 1) //There are no more regexes ahead
                            {
                                //It's the end of the document, close the textblock right here
                                currentNode = CloseControlBlock(currentNode);
                            }

                            break;
                        }
                    case KeyWords.ENDSCAN:
                        {

                            if (currentNode is TextBlock)
                            {
                                currentNode = CloseControlBlock(currentNode, match);
                            }

                            currentNode = CloseControlBlock(currentNode, match);

                            currentNode = CreateTextBlock(match, currentNode);
                            if (currRegex == matchCollection.Count - 1) //There are no more regexes ahead
                            {
                                //It's the end of the document, close the textblock right here
                                currentNode = CloseControlBlock(currentNode);
                            }

                            break;
                        }
                }
                currRegex++;

                #endregion
            }
            IControlBlock trailingText = CreateTextBlock(matchCollection[currRegex - 1], currentNode);
            currentNode = CloseControlBlock(trailingText/*, matchCollection[currRegex - 1].Index*/);

        }

        private IControlBlock CreateTextBlock(Match match, IControlBlock parentNode)
        {
            IControlBlock textBlock = new TextBlock(false);
            textBlock.Start = match.Index + match.Length;
            textBlock.DocumentParser = this;
            textBlock.Parent = parentNode;
            textBlock.OpenRegEx = match;
            parentNode.Children.Add(textBlock);
            return textBlock;
        }

        private IControlBlock CreateScan(IControlBlock currentNode, Match match, bool hasCondition)
        {
            IControlBlock scanBlock = new ScanBlock(match, hasCondition, this);
            scanBlock.OpenRegEx = match;
            scanBlock.MatchStart = match.Index;
            scanBlock.Start = match.Index + match.Length;
            ((ScanBlock)scanBlock).Table = DataManager.Tables[((ScanBlock)scanBlock).TableName].Table;
            scanBlock.DocumentParser = this;
            scanBlock.Parent = currentNode;
            currentNode.Children.Add(scanBlock);
            currentNode = scanBlock;
            return currentNode;
        }


        /// <summary>
        /// Prepares the text adding marks at the end of each regular expression (Programming sentence)
        /// so they can be deleted easily after generating the report.-
        /// </summary>
        private void PreprocessText()
        {
            //Check to see if must work with RTF or plain text format.
            idFmt = TextFormat == eTextFormat.Text ? (IDocumentFormat)new TextFormat() : (IDocumentFormat)new RTFFormat();

            //Get Matches for any possible placeholder
            Regex regex0 = CommonMethods.GetRegex(KeywordsRegExString);
            MatchCollection matchCollInit = regex0.Matches(_RTFText.ToString());

            //Replaces every 'new line' sequence with '\DEL' sequence.
            foreach (Match m in matchCollInit)
            {
                int i = RTFInput.ToString().IndexOf(idFmt.NewLine, m.Index);
                if (i >= 0)
                {
                    RTFInput = RTFInput.Remove(i, idFmt.NewLine.Length);
                    RTFInput = RTFInput.Insert(i, "\\DEL");
                }
            }
            RTFInput = RTFInput.Replace("\\DEL", "");
        }

        private IControlBlock CloseControlBlock(IControlBlock closingNode, Match match)
        {
            IControlBlock cb = (IControlBlock)closingNode;
            cb.MatchEnd = match.Index + match.Length;
            cb.CloseRegEx = match;
            closingNode.End = match.Index;
            closingNode.InnerText = RTFInput.ToString().InnerString(closingNode.Start, closingNode.End).Trim(); // TODO: CAMBIO RTFInput.ToString().InnerString(closingNode.Start, closingNode.End);

            closingNode = closingNode.Parent;
            return closingNode;
        }

        private IControlBlock CloseControlBlock(IControlBlock closingNode/*, int matchIndex*/)
        {
            IControlBlock cb = closingNode;
            cb.MatchEnd = RTFInput.Length;
            cb.CloseRegEx = null;
            closingNode.End = RTFInput.Length;
            closingNode.InnerText = RTFInput.ToString().InnerString(closingNode.Start, closingNode.End).Trim(); // TODO: CAMBIO RTFInput.ToString().InnerString(closingNode.Start, closingNode.End);

            closingNode = closingNode.Parent;
            return closingNode;
        }

        private KeyWords GetKeyWord(Match match)
        {
            return CommonMethods.GetKeyWord(match);
        }

        internal void RaiseAfterScanRecordEvent(AfterScanRecordEventArgs e)
        {
            Creator.RaiseAfterScanRecordEvent(e);
        }

        internal void RaiseBeforeScanRecordEvent(BeforeScanRecordEventArgs e)
        {
            Creator.RaiseBeforeScanRecordEvent(e);
        }

        internal void RaiseScanEndedEvent(ScanEndedEventArgs e)
        {
            Creator.RaiseScanEndedEvent(e);
        }

        internal void RaiseScanStartEvent(ScanStartEventArgs e)
        {
            Creator.RaiseScanStartEvent(e);
        }
    }
}