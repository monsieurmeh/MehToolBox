using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MehToolBox.Extensions
{
    public static class GameObjectExtensions
    {
        public static T GetOrCreateComponent<T>(this GameObject go) where T : Component
        {
            T t = go.GetComponent<T>();
            if (t != null) return t;
            return go.AddComponent<T>();
        }
    }
}
