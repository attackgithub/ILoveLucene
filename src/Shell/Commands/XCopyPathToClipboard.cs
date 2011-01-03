﻿using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using Core.Abstractions;

namespace ILoveLucene.Commands
{
    /// <summary>
    /// the X is just so that this command ends up on the end of the box
    /// </summary>
    [Export(typeof(IActOnItem))]
    public class XCopyPathToClipboard : BaseActOnTypedItem<FileInfo>
    {
        [Import]
        public ILog Log { get; set; }

        public override void ActOn(FileInfo item)
        {
            // TODO: calling onuithread should be wrapped in order to trap exceptions
            Caliburn.Micro.Execute.OnUIThread(() =>
                                                  {
                                                      try
                                                      {
                                                          Clipboard.SetText(item.FullName);
                                                      }
                                                      catch (Exception e)
                                                      {
                                                          Log.Error(e, "Error setting clipboard:{0}", e.Message);
                                                      }
                                                  });
        }
    }
}