﻿//------------------------------------------------------------------------------
// <auto-generated>
//     このコードはツールによって生成されました。
//     ランタイム バージョン:4.0.30319.42000
//
//     このファイルへの変更は、以下の状況下で不正な動作の原因になったり、
//     コードが再生成されるときに損失したりします。
// </auto-generated>
//------------------------------------------------------------------------------

namespace Sekiban.Addon.Tenant.Properties {

    /// <summary>
    ///   ローカライズされた文字列などを検索するための、厳密に型指定されたリソース クラスです。
    /// </summary>
    // このクラスは StronglyTypedResourceBuilder クラスが ResGen
    // または Visual Studio のようなツールを使用して自動生成されました。
    // メンバーを追加または削除するには、.ResX ファイルを編集して、/str オプションと共に
    // ResGen を実行し直すか、または VS プロジェクトをビルドし直します。
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class ExceptionMessages {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal ExceptionMessages() {
        }
        
        /// <summary>
        ///   このクラスで使用されているキャッシュされた ResourceManager インスタンスを返します。
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("JJ.Sekiban.Extensions.Properties.ExceptionMessages", typeof(ExceptionMessages).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   すべてについて、現在のスレッドの CurrentUICulture プロパティをオーバーライドします
        ///   現在のスレッドの CurrentUICulture プロパティをオーバーライドします。
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   存在しない {0} です。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string AggregateNotExistsValidationErrorMessage {
            get {
                return ResourceManager.GetString("AggregateNotExistsValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} はすでに存在します。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string AlreadyExistsValidationError {
            get {
                return ResourceManager.GetString("AlreadyExistsValidationError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   コマンド {0} の実行に失敗したか、実行結果を取得できませんでした。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string CommandFailedExceptionMessage {
            get {
                return ResourceManager.GetString("CommandFailedExceptionMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} の設定がありません。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string ConfigurationNotExistsException {
            get {
                return ResourceManager.GetString("ConfigurationNotExistsException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} &apos;{1}&apos; はすでに登録されています。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string ConflictValueValidationErrorMessage {
            get {
                return ResourceManager.GetString("ConflictValueValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} は {1} でなければなりません。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string InvalidCharacterValidationErrorMessage {
            get {
                return ResourceManager.GetString("InvalidCharacterValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} の形式が無効です。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string InvalidFormatValidationErrorMessage {
            get {
                return ResourceManager.GetString("InvalidFormatValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   無効なパラメーターです。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string InvaliParameterValidationErrorMessage {
            get {
                return ResourceManager.GetString("InvaliParameterValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} は{1}文字以内でなければなりません。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string OverflowValidationErrorMessage {
            get {
                return ResourceManager.GetString("OverflowValidationErrorMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   {0} は必須入力です。 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        internal static string RequiredFieldValidationErrorMessage {
            get {
                return ResourceManager.GetString("RequiredFieldValidationErrorMessage", resourceCulture);
            }
        }
    }
}