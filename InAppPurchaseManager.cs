// Author : Soundpiercer 2019-11-15
// soundpiercer@gmail.com
//
// [Purchase Process Steps]
// Client --> Unity IAP --> App Store Transaction (Google/Apple) --> Unity IAP --> Client --> Server --> Client
// Please remind these steps!
//
// [How to Use]
// 1. Attach this script into your GameObject
// 2. Initialize this IAP Manager after user login has completed
// 3. Call BuyProductID from outside to start purchase

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Security;

/// <summary>
/// Unity IAP Manager with advanced features
/// 1. Client Side Receipt Validation
/// 2. Server Side Purchase & Receipt Validation
/// 3. Client Side Purchase Restoration (App Store SKU Transaction)
/// 4. Server Side Purchase Restoration (Send API Request when Client Side Purchase Restore occurs)
/// </summary>
public class InAppPurchaseManager : MonoBehaviour, IStoreListener
{
    #region Singleton (Simplified)
    public static InAppPurchaseManager Instance;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
            Destroy(gameObject);

        SetToDontDestroyOnLoad();
    }

    private void SetToDontDestroyOnLoad()
    {
        DontDestroyOnLoad(gameObject);
    }
    #endregion

    // Unity IAP
    private static IStoreController storeController;          // The Unity Purchasing system.
    private static IExtensionProvider storeExtensionProvider; // The store-specific Purchasing subsystems.

    // Managed Products.
    public List<string> managedProducts = new List<string>();

    /// <summary>
    /// is your purchase in Client Side Steps? (Client - IAP - App Store - IAP - Client)
    /// </summary>
    [HideInInspector]
    public bool isBuyingInClient;

    /// <summary>
    /// is your purchase in Full Process Steps? (Client - IAP - App Store - IAP - Client - Server - Client)
    /// </summary>
    [HideInInspector]
    public bool isPurchaseUnderProcess;

    // Purchase Restoration Popup
    [HideInInspector]
    public bool shouldShowPurchaseRestorePopup;
    private string restoredProductName;
    private bool hasPopupShown;

    private IEnumerator Start()
    {
        // Should define managed products list to be used.
        InitializeManagedProducts();

        // Unity IAP
        if (storeController == null)
        {
            InitializePurchasing();
        }

        // Force to release all pending transactions in first play
        // This feature is useful when you're UPDATING PURCHASE RESTORE IN YOUR LIVE CLIENT.
        yield return StartCoroutine(ReleaseAllUnfinishedTransactionsOnFirstRunEnumerator());
    }

    #region Start Subroutines
    private void InitializeManagedProducts()
    {
        List<string> productIdsInYourDB = new List<string>();

        // ###################### YOUR LOGIC GOES HERE
        // ###################### productIdsInYourDB = (Fetched productID list from your DB)

        managedProducts = productIdsInYourDB;
    }

    private bool HasInitialized()
    {
        return storeController != null && storeExtensionProvider != null;
    }

    private void InitializePurchasing()
    {
        if (HasInitialized())
            return;

        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

#if UNITY_ANDROID
        string storeName = GooglePlay.Name;
#elif UNITY_IOS
        string storeName = AppleAppStore.Name;
#endif

        // Add managedProducts to the builder.
        foreach (string productName in managedProducts)
        {
            builder.AddProduct(
                productName,
                ProductType.Consumable,
                new IDs {{ productName, storeName }
                });
        }

        UnityPurchasing.Initialize(this, builder);
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        Debug.Log("[In-App Purchase Manager] >>>>>> OnInitialized: PASS");

        storeController = controller;
        storeExtensionProvider = extensions;
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.LogError("[In-App Purchase Manager] >>>>>> OnInitializeFailed InitializationFailureReason:" + error);
    }

    private IEnumerator ReleaseAllUnfinishedTransactionsOnFirstRunEnumerator()
    {
        // Unity IAP Initialization is Asynchronous, so we should wait
        yield return new WaitUntil(HasInitialized);

        // Some of your LIVE SERVICE users may already received the items via your CUSTOMER SERVICE,
        // so we treat all pending transactions as COMPENSATED.
        // this process is executed ONLY ONCE.
        if (PlayerPrefs.GetInt("FirstRunPurchaseRestorationHasExecuted", 0) == 0)
        {
            PlayerPrefs.SetInt("FirstRunPurchaseRestorationHasExecuted", 1);
            ReleaseAllUnfinishedUnityIAPTransactions();
        }
    }
    #endregion

    #region Unity IAP (Transaction Control)
    public IEnumerator BuyProductID(string productId)
    {
        if (HasInitialized())
        {
            Product product = storeController.products.WithID(productId);

            if (product != null && product.availableToPurchase)
            {
                Debug.Log(string.Format("[In-App Purchase Manager] <<<<<< Purchasing product asychronously: '{0}'", product.definition.id));
                storeController.InitiatePurchase(product);
            }
            else
            {
                Debug.LogError("[In-App Purchase Manager] >>>>>> BuyProductID: FAIL. Not purchasing product, either is not found or is not available for purchase");
            }
        }
        else
        {
            Debug.LogError("[In-App Purchase Manager] >>>>>> BuyProductID FAIL. Not initialized.");
        }

        yield return null;
    }

    // Automatically Called by Unity IAP when
    // 1. NORMAL PURCHASE (Charging is done, waiting for transaction)
    // 2. PURCHASE RESTORATION (Restoring Purchase on Init)
    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        // Client Side Receipt Validation
        bool validPurchase = CheckReceipt(args.purchasedProduct);

        if (validPurchase)
        {
            Debug.Log(string.Format("[In-App Purchase Manager] >>>>>> ProcessPurchase: PASS. Product: '{0}'", args.purchasedProduct.definition.id));
        }
        else
        {
            Debug.LogError(string.Format("[In-App Purchase Manager] >>>>>> ProcessPurchase: Fail (receipt Valid Error)  Product: '{0}'", args.purchasedProduct.definition.id));
        }

        isBuyingInClient = false;

        // NORMAL PURCHASE
        if (isPurchaseUnderProcess)
        {
            Debug.LogWarning("[In-App Purchase Manager] <<<<<< Pending sku start!");
            return PurchaseProcessingResult.Pending;

            // ########################################################################
            // ## YOUR SERVER LOGIC GOES HERE : VALIDATE THE RECEIPT AND GIVE THE ITEMS
            // ########################################################################
            // YourServerLogic(args.purchaseProduct.definition.id, args.purchaseProduct, ReleaseAllUnfinishedUnityIAPTransactions)
        }
        // PURCHASE RESTORATION
        else
        {
            Debug.LogWarning("[In-App Purchase Manager] <<<<<< Restoring Unfinished Transactions!");
            Product product = args.purchasedProduct;
            string productId = product.definition.id;
            StartCoroutine(PurchaseRestoreEnumerator(productId, product));

            return PurchaseProcessingResult.Complete;
        }
    }

    private bool CheckReceipt(Product product)
    {
        var validator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
        try
        {
            Debug.Log(product.receipt);
            var result = validator.Validate(product.receipt);
        }
        catch (IAPSecurityException)
        {
            Debug.LogError("Invalid Receipt");
            return false;
        }

        return true;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.LogErrorFormat(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason));
        isBuyingInClient = false;
    }
    #endregion

    #region Purchase Restoration
    private IEnumerator PurchaseRestoreEnumerator(string productId, Product product)
    {
        // ########################################################################
        // ## YOUR SERVER LOGIC GOES HERE : VALIDATE THE RECEIPT AND GIVE THE ITEMS
        // ########################################################################
        // YourServerLogic(productId, product, OnServerConnectionSuccess)

        yield return null;
        restoredProductName = productId;
    }

    private void OnServerConnectionSuccess(bool isSuccess)
    {
        if (isSuccess)
        {
            Debug.LogWarning("[In-App Purchase Manager : API Connection] >>>>>> Server Connection Success!");
            shouldShowPurchaseRestorePopup = true;
        }
        else
        {
            Debug.LogError("[In-App Purchase Manager : API Connection] >>>>>> Server Connection Error! (Duplicated Request, Already Confirmed Receipt, or else)");
        }
    }

    /*
    /// <summary>
    /// Extract Payload part from your receipt for Server Side Receipt Validation.
    /// </summary>
    public static string ExtractPayload(string receipt)
    {
        string[] separators = { "\"Payload\":\"" };
        string[] splittedStrings = receipt.Split(separators, StringSplitOptions.None);

        receipt = splittedStrings[1].Replace("\"}", "");
        return receipt;
    }
    */

    /// <summary>
    /// Releases all unfinished unity IAP transactions. Works on unfinished transactions (Pending) only
    /// </summary>
    public void ReleaseAllUnfinishedUnityIAPTransactions()
    {
        foreach (string productId in managedProducts)
        {
            Product p = storeController.products.WithID(productId);
            if (p != null)
                storeController.ConfirmPendingPurchase(p);
        }

        isPurchaseUnderProcess = false;
        shouldShowPurchaseRestorePopup = false;
        restoredProductName = string.Empty;
    }
    #endregion

    #region Show Purchase Restoration Popup
    // This method is called from outside of the manager.
    // You can call this method anytime when you want. 
    // ENTERING TITLE / LOBBY SCENE is strongly recommended.
    public void CheckAndShowWindowIfRestoredPurchaseExists(Action showPopupCallback = null)
    {
        if (shouldShowPurchaseRestorePopup)
            StartCoroutine(ShowEnumerator(showPopupCallback));
    }

    private IEnumerator ShowEnumerator(Action showPopupCallback = null)
    {
        // Show only once!
        if (hasPopupShown) yield break;

        // Check ProductID Validity
        if (!managedProducts.Contains(restoredProductName))
        {
            yield break;
        }

        if (string.IsNullOrEmpty(restoredProductName))
        {
            yield break;
        }

        // Show Popup.
        showPopupCallback();

        // Set Variables.
        shouldShowPurchaseRestorePopup = false;
        restoredProductName = string.Empty;
        hasPopupShown = true;
    }
    #endregion
}
