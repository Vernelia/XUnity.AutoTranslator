﻿using UnityEngine;
using XUnity.Common.Utilities;

namespace XUnity.ResourceRedirector
{
   /// <summary>
   /// Utility methods for AssetBundles.
   /// </summary>
   public static class AssetBundleHelper
   {
      /// <summary>
      /// Creates an empty AssetBundle with a randomly generated CAB identifier.
      /// </summary>
      /// <returns>The empty asset bundle with a random CAB identifier.</returns>
      public static AssetBundle CreateEmptyAssetBundle()
      {
         var buffer = Properties.Resources.empty;
         CabHelper.RandomizeCab( buffer );
         return AssetBundle.LoadFromMemory( buffer );
      }
   }
}